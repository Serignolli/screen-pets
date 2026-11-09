package main

import (
	"image/color"
	"math"
	"math/rand"
	"strings"

	"github.com/hajimehoshi/ebiten/v2"
	"github.com/hajimehoshi/ebiten/v2/text/v2"
	"github.com/hajimehoshi/ebiten/v2/vector"
)

// Uma faixa colada numa borda da tela (port de src/Stage.cs).
// Toda a lógica acontece num espaço "canônico": uma faixa deitada de comprimento length e espessura thick,
// com o chão embaixo (y = thick). A matriz m leva esse espaço para a faixa: no topo ele fica de cabeça
// para baixo (andando no teto) e nas laterais ele gira 90° (escalando a parede).

type Zone int

const (
	ZoneGround Zone = iota
	ZoneTop
	ZoneLeft
	ZoneRight
)

const (
	sWalk = iota
	sWork
	sIdle
	sJump
)

// Quem entra num palco: personagem, cor, quantos e tamanho.
type Cast struct {
	Ch          *Character
	Skin        string
	Count, Size int
}

// O que vale para todos de uma mesma turma dentro do palco.
type troupe struct {
	ch                          *Character
	scale, px, spriteW, feetOff int
	upright                     bool // voadores no alto e nas laterais voam sem girar junto com a borda
	flyBase, flyRange           int
	gravity                     float32
	symbolFace                  text.Face
}

type worker struct {
	t                                                          *troupe
	X, Target, Timer, FrameTimer, Delay, SpeedMul, BubbleTimer float32
	Y, VY, Squash, MeetCooldown                                float32 // Y: altura do pulo (para longe da borda)
	Alt, AltTarget                                             float32 // altitude extra de quem voa
	IdleHold, Phase                                            float32
	Dir, State, Frame, TopRow                                  int
	Carrying, Looked                                           bool
	WalkR, WalkL, WorkR, WorkL, IdleR, IdleL                   []*ebiten.Image
	WorksR, WorksL                                             [][]*ebiten.Image // um conjunto por tipo de trabalho
	WorkFx                                                     []string
	CarryR, CarryL                                             *ebiten.Image
	Bubble, Effect                                             string
}

type particle struct {
	X, Y, VX, VY, GX, GY, Life, MaxLife, Grow, Crawl float32
	C                                                color.RGBA
	Text                                             string
	Face                                             text.Face
	Size                                             int
	Bug, Floor                                       bool
}

// Matriz como a System.Drawing.Drawing2D.Matrix: x' = m11*x + m21*y + dx, y' = m12*x + m22*y + dy.
type matrix struct{ m11, m12, m21, m22, dx, dy float32 }

func (m matrix) point(x, y float32) (float32, float32) {
	return m.m11*x + m.m21*y + m.dx, m.m12*x + m.m22*y + m.dy
}

func (m matrix) vec(x, y float32) (float32, float32) { return m.m11*x + m.m21*y, m.m12*x + m.m22*y }

func (m matrix) geoM() ebiten.GeoM {
	var g ebiten.GeoM
	g.SetElement(0, 0, float64(m.m11))
	g.SetElement(0, 1, float64(m.m21))
	g.SetElement(0, 2, float64(m.dx))
	g.SetElement(1, 0, float64(m.m12))
	g.SetElement(1, 1, float64(m.m22))
	g.SetElement(1, 2, float64(m.dy))
	return g
}

type rect struct{ X, Y, W, H int }

type Stage struct {
	zone        Zone
	cfg         *Config
	workers     []*worker
	parts       []*particle
	rnd         *rand.Rand
	dev         float64
	thick       int
	length      int
	b           rect // a faixa, em pixels da janela
	m           matrix
	elapsed     float64
	nextBubble  float64
	bubbleFace  text.Face
	bubbleSpace int
}

func (s *Stage) horizontal() bool { return s.zone == ZoneGround || s.zone == ZoneTop }
func (s *Stage) bubbles() bool    { return s.cfg.Bubbles && s.horizontal() }

func newStage(zone Zone, casts []Cast, cfg *Config, dev float64, rnd *rand.Rand) *Stage {
	s := &Stage{zone: zone, cfg: cfg, dev: dev, rnd: rnd}
	s.bubbleFace = pixelFace(12 * dev)
	s.bubbleSpace = int(34 * dev)
	s.nextBubble = 4 + rnd.Float64()*6

	need, index := 0, 0
	for _, cast := range casts {
		t := s.makeTroupe(cast)
		ch := t.ch
		across := (ch.FeetRow - ch.TopRow + 1) * t.px
		if t.upright && !s.horizontal() {
			across = t.spriteW
		}
		room := across + t.flyBase + t.flyRange + 3*t.scale
		if !t.upright {
			room += 16 * t.scale
		}
		need = max(need, room)

		render := func(skin *Skin, kind string, flip bool) []*ebiten.Image {
			var out []*ebiten.Image
			for _, im := range ch.Render(skin, kind, t.px, flip) {
				out = append(out, ebiten.NewImageFromImage(im))
			}
			return out
		}
		type set struct {
			walkR, walkL, idleR, idleL []*ebiten.Image
			worksR, worksL             [][]*ebiten.Image
			fx                         []string
		}
		cache := map[string]*set{}
		// Misturando cores, cada um começa com uma diferente antes de repetir.
		order := append([]*Skin(nil), ch.Skins...)
		rnd.Shuffle(len(order), func(i, j int) { order[i], order[j] = order[j], order[i] })
		defProp := getProp(ch.CarryOf(ch.Skins[0]))
		for i := 0; i < cast.Count; i, index = i+1, index+1 {
			skin := ch.GetSkin(cast.Skin)
			if cast.Skin == "*" {
				skin = order[i%len(order)]
			}
			st, ok := cache[skin.ID]
			if !ok {
				st = &set{walkR: render(skin, "andar", false), walkL: render(skin, "andar", true),
					idleR: render(skin, "parado", false), idleL: render(skin, "parado", true)}
				for _, k := range ch.WorkKinds(skin) {
					st.worksR = append(st.worksR, render(skin, k, false))
					st.worksL = append(st.worksL, render(skin, k, true))
					st.fx = append(st.fx, ch.EffectOf(skin, k))
				}
				cache[skin.ID] = st
			}
			w := &worker{t: t, WalkR: st.walkR, WalkL: st.walkL, IdleR: st.idleR, IdleL: st.idleL,
				WorksR: st.worksR, WorksL: st.worksL, WorkFx: st.fx, TopRow: skin.TopRow, IdleHold: 0.8}
			s.pickWork(w)
			carry := getProp(ch.CarryOf(skin))
			if carry == nil {
				carry = defProp
			}
			if carry != nil && !t.upright {
				w.CarryR = ebiten.NewImageFromImage(renderPixels(carry.Rows, carry.Colors, t.scale, false))
				w.CarryL = ebiten.NewImageFromImage(renderPixels(carry.Rows, carry.Colors, t.scale, true))
			}
			w.SpeedMul = 0.8 + rnd.Float32()*0.4
			w.Delay = float32(index)*0.7 + rnd.Float32()*0.5
			w.Dir = 1
			if index%2 == 1 {
				w.Dir = -1
			}
			w.State = sWalk
			w.MeetCooldown = 2
			w.Phase = rnd.Float32() * math.Pi * 2
			w.Alt = rnd.Float32() * float32(t.flyRange)
			w.AltTarget = w.Alt
			w.X = float32(math.NaN())
			s.workers = append(s.workers, w)
		}
	}
	s.thick = need + 2
	if s.bubbles() {
		s.thick += s.bubbleSpace
	}
	return s
}

func (s *Stage) makeTroupe(cast Cast) *troupe {
	ch := cast.Ch
	t := &troupe{ch: ch}
	t.scale = max(1, int(math.Round(float64(max(1, cast.Size))*s.dev)))
	t.px = ch.PixelScale(t.scale)
	t.spriteW = ch.Width * t.px
	t.feetOff = (ch.FeetRow + 1) * t.px
	t.upright = ch.Flies() && s.zone != ZoneGround
	visH := (ch.FeetRow - ch.TopRow + 1) * t.px
	switch {
	case !ch.Flies():
		t.flyBase = 0
	case t.upright:
		t.flyBase = 2 * t.scale
	default:
		t.flyBase = ch.FlyHeight * t.px
	}
	if ch.Flies() {
		t.flyRange = visH
	}
	t.gravity = 900 * float32(t.scale) / 3
	t.symbolFace = pixelFace((8 + float64(max(1, cast.Size))*2) * s.dev)
	return t
}

// ---------- posição ----------

// place cola a faixa na borda da área de trabalho (wa, em pixels da janela).
func (s *Stage) place(wa rect) {
	var b rect
	var length int
	switch s.zone {
	case ZoneTop:
		b, length = rect{wa.X, wa.Y, wa.W, s.thick}, wa.W
	case ZoneLeft:
		b, length = rect{wa.X, wa.Y, s.thick, wa.H}, wa.H
	case ZoneRight:
		b, length = rect{wa.X + wa.W - s.thick, wa.Y, s.thick, wa.H}, wa.H
	default:
		b, length = rect{wa.X, wa.Y + wa.H - s.thick, wa.W, s.thick}, wa.W
	}
	if b == s.b && length == s.length {
		return
	}
	s.b, s.length = b, length
	th, ln := float32(s.thick), float32(length)
	switch s.zone {
	case ZoneTop:
		s.m = matrix{1, 0, 0, -1, 0, th}
	case ZoneLeft:
		s.m = matrix{0, -1, -1, 0, th, ln}
	case ZoneRight:
		s.m = matrix{0, -1, 1, 0, 0, ln}
	default:
		s.m = matrix{1, 0, 0, 1, 0, 0}
	}
	for _, w := range s.workers {
		if math.IsNaN(float64(w.X)) {
			continue
		}
		w.X = max(float32(-w.t.spriteW), min(ln+float32(w.t.spriteW), w.X))
		if w.Target > ln {
			w.Target = s.randomTarget(w)
		}
	}
}

// ---------- ciclo ----------

func (s *Stage) Step(dt float32) {
	s.elapsed += float64(dt)
	for _, w := range s.workers {
		s.updateWorker(w, dt)
	}
	s.checkMeetings()
	s.updateParticles(dt)
	s.updateBubbles(dt)
}

func (s *Stage) randomTarget(w *worker) float32 {
	margin := float32(w.t.spriteW) * 0.75
	span := max(1, float32(s.length)-2*margin)
	return margin + s.rnd.Float32()*span
}

func (s *Stage) decide(w *worker) {
	r := s.rnd.Float64()
	flies := w.t.ch.Flies()
	w.Frame, w.FrameTimer, w.Looked = 0, 0, false
	work := 0.42
	if flies {
		work = 0.3
	}
	switch {
	case r < work:
		w.State = sWork
		s.pickWork(w)
		w.Timer = 2.5 + s.rnd.Float32()*4
		if w.Carrying {
			w.Carrying = false
			s.spawn("poeira", w, 3)
		}
	case r < 0.8:
		w.State = sWalk
		w.Target = s.randomTarget(w)
		w.AltTarget = s.rnd.Float32() * float32(w.t.flyRange)
		w.Carrying = w.CarryR != nil && s.rnd.Float64() < 0.45
	case r < 0.88 && w.Y <= 0 && !flies:
		// pulinho de alegria no lugar
		w.State = sJump
		s.launch(w, 5+s.rnd.Float32()*3)
	default:
		w.State = sIdle
		w.Timer = 0.9 + s.rnd.Float32()*2.2
		if s.rnd.Float64() < 0.5 {
			w.Dir = -w.Dir
		}
	}
}

// Sorteia qual trabalho fazer (quando o personagem tem mais de um).
func (s *Stage) pickWork(w *worker) {
	if len(w.WorksR) == 0 {
		w.WorkR, w.WorkL, w.Effect = w.IdleR, w.IdleL, ""
		return
	}
	k := s.rnd.Intn(len(w.WorksR))
	w.WorkR, w.WorkL, w.Effect = w.WorksR[k], w.WorksL[k], w.WorkFx[k]
}

// Pulo de height pontos de altura.
func (s *Stage) launch(w *worker, height float32) {
	if w.Y > 0 {
		return
	}
	w.VY = float32(math.Sqrt(float64(2 * w.t.gravity * height * float32(w.t.scale))))
	w.Y = 0.01
}

func (s *Stage) updateWorker(w *worker, dt float32) {
	if w.Delay > 0 {
		w.Delay -= dt
		return
	}
	t := w.t
	if math.IsNaN(float64(w.X)) {
		if w.Dir > 0 {
			w.X = float32(-t.spriteW)
		} else {
			w.X = float32(s.length + t.spriteW)
		}
		w.Target = s.randomTarget(w)
		w.State = sWalk
	}
	if w.BubbleTimer > 0 {
		w.BubbleTimer -= dt
		if w.BubbleTimer <= 0 {
			w.Bubble = ""
		}
	}
	if w.Squash > 0 {
		w.Squash -= dt
	}
	if w.MeetCooldown > 0 {
		w.MeetCooldown -= dt
	}
	w.Alt += (w.AltTarget - w.Alt) * min(1, dt*0.8)
	w.FrameTimer += dt

	// física do pulo
	if w.Y > 0 {
		w.VY -= t.gravity * dt
		w.Y += w.VY * dt
		if w.Y <= 0 {
			w.Y, w.VY, w.Squash = 0, 0, 0.12
			if !t.ch.Flies() {
				s.spawn("poeira", w, 2)
			}
			if w.State == sJump {
				s.decide(w)
			}
		}
	}

	pace := s.cfg.Speed * t.ch.Pace
	speed := float32(34.0*float64(t.scale)/3.0*pace) * w.SpeedMul
	// desenhos com mais quadros trocam mais rápido, para o passo continuar do mesmo tamanho
	step := float32(0.18)
	if len(w.WalkR) >= 8 {
		step = 0.095
	} else if len(w.WalkR) >= 4 {
		step = 0.13
	}
	step /= float32(s.cfg.Speed * math.Max(0.5, t.ch.Pace))
	switch w.State {
	case sWalk:
		dx := w.Target - w.X
		if float32(math.Abs(float64(dx))) <= speed*dt {
			w.X = w.Target
			s.decide(w)
			break
		}
		w.Dir = 1
		if dx < 0 {
			w.Dir = -1
		}
		w.X += float32(w.Dir) * speed * dt
		if w.FrameTimer > step {
			w.FrameTimer = 0
			w.Frame = (w.Frame + 1) % len(w.WalkR)
		}
	case sWork:
		w.Timer -= dt
		// animações quadro a quadro andam mais rápido que as de 2 quadros
		ws := float32(0.3)
		if len(w.WorkR) >= 4 {
			ws = 0.11
		}
		if w.FrameTimer > ws/float32(math.Max(0.5, s.cfg.Speed)) {
			w.FrameTimer = 0
			w.Frame = (w.Frame + 1) % len(w.WorkR)
			if w.Frame == len(w.WorkR)-1 {
				s.spawn(w.Effect, w, 0)
			}
		}
		if w.Timer <= 0 {
			s.decide(w)
		}
	case sJump:
	default:
		w.Timer -= dt
		if t.ch.Flies() {
			// quem voa continua batendo as asas parado no ar
			if w.FrameTimer > step*1.3 {
				w.FrameTimer = 0
				w.Frame = (w.Frame + 1) % len(w.WalkR)
			}
		} else {
			s.animateIdle(w)
		}
		// olha para o outro lado no meio da pausa
		if !w.Looked && w.Timer < 0.6 && s.rnd.Float64() < 0.5 {
			w.Dir = -w.Dir
			w.Looked = true
		}
		if w.Timer <= 0 {
			s.decide(w)
		}
	}
}

// "parado" com vários quadros: o primeiro fica um tempo (respirando) e os outros passam rápido (uma piscada).
func (s *Stage) animateIdle(w *worker) {
	if len(w.IdleR) < 2 {
		w.Frame = 0
		return
	}
	hold := float32(0.09)
	if w.Frame == 0 {
		hold = w.IdleHold
	}
	if w.FrameTimer < hold {
		return
	}
	w.FrameTimer = 0
	w.Frame = (w.Frame + 1) % len(w.IdleR)
	if w.Frame == 0 {
		w.IdleHold = 1.2 + s.rnd.Float32()*2.2
	}
}

func visible(w *worker) bool { return w.Delay <= 0 && !math.IsNaN(float64(w.X)) }

// Quando dois se cruzam andando, às vezes param, se olham e se cumprimentam.
func (s *Stage) checkMeetings() {
	for i, a := range s.workers {
		if a.State != sWalk || !visible(a) || a.MeetCooldown > 0 {
			continue
		}
		for _, b := range s.workers[i+1:] {
			if b.State != sWalk || !visible(b) || b.MeetCooldown > 0 {
				continue
			}
			if a.Dir == b.Dir || a.t.ch.Flies() != b.t.ch.Flies() {
				continue
			}
			sw := float32(a.t.spriteW+b.t.spriteW) / 2
			gap := b.X - a.X
			ag := float32(math.Abs(float64(gap)))
			if ag > sw*0.9 || ag < sw*0.5 || (gap > 0) != (a.Dir > 0) {
				continue
			}
			a.MeetCooldown, b.MeetCooldown = 10, 10
			if s.rnd.Float64() > 0.45 {
				continue
			}
			for _, w := range []*worker{a, b} {
				w.State, w.Timer, w.Frame, w.Looked = sIdle, 1.3+s.rnd.Float32()*0.5, 0, true
			}
			a.Dir = -1
			if gap > 0 {
				a.Dir = 1
			}
			b.Dir = -a.Dir
			hopper := a
			if s.rnd.Intn(2) == 0 {
				hopper = b
			}
			if !hopper.t.ch.Flies() {
				s.launch(hopper, 3)
			}
			sym := "♥"
			if s.rnd.Intn(3) == 0 {
				sym = "!"
			}
			s.add(a, (b.X-a.X)/2-float32(a.t.scale)*2, -float32(s.visH(a))-float32(a.t.scale)*2, s.r(-6, 6), -22, 0, 1.3,
				s.pick("#ec407a", "#ffca28"), sym, 0)
			break
		}
	}
}

func (s *Stage) updateBubbles(dt float32) {
	if !s.bubbles() {
		return
	}
	s.nextBubble -= float64(dt)
	if s.nextBubble > 0 {
		return
	}
	var cand []*worker
	for _, w := range s.workers {
		if visible(w) && w.Bubble == "" && len(w.t.ch.Speech) > 0 && w.X > 0 && w.X < float32(s.length) {
			cand = append(cand, w)
		}
	}
	if len(cand) > 0 {
		w := cand[s.rnd.Intn(len(cand))]
		w.Bubble = w.t.ch.Speech[s.rnd.Intn(len(w.t.ch.Speech))]
		w.BubbleTimer = 3.2
	}
	s.nextBubble = 10 + s.rnd.Float64()*14
}

// ---------- geometria ----------

func (s *Stage) visH(w *worker) int { return (w.t.ch.FeetRow - w.TopRow + 1) * w.t.px }

// Altura de quem voa: base + altitude própria + um sobe e desce suave (bater de asas).
func (s *Stage) fly(w *worker) float32 {
	t := w.t
	if !t.ch.Flies() {
		return 0
	}
	return float32(t.flyBase) + w.Alt + float32(math.Sin(s.elapsed*3.2+float64(w.Phase))*float64(t.scale)*1.3)
}

func (s *Stage) height(w *worker) float32 { return w.Y + s.fly(w) }

// Para onde o desenho olha na tela, para quem voa em pé.
func (s *Stage) facing(w *worker) int {
	switch s.zone {
	case ZoneRight:
		return -1
	case ZoneLeft:
		return 1
	}
	return w.Dir
}

func (s *Stage) face(w *worker) int {
	if w.t.upright {
		return s.facing(w)
	}
	return w.Dir
}

// Quem voa em pé: canto de cima do desenho, na faixa.
func (s *Stage) uprightTopLeft(w *worker, bw int) (float32, float32) {
	t := w.t
	visH := s.visH(w)
	across := float32(t.spriteW)
	if s.horizontal() {
		across = float32(visH)
	}
	cx, cy := s.m.point(w.X, float32(s.thick)-s.height(w)-across/2)
	return cx - float32(bw)/2, cy - (float32(w.TopRow*t.px) + float32(visH)/2)
}

// Os pés, na faixa (origem dos efeitos).
func (s *Stage) feetOnScreen(w *worker) (float32, float32) {
	if !w.t.upright {
		return s.m.point(w.X, float32(s.thick)-s.height(w))
	}
	bw := w.WalkR[0].Bounds().Dx()
	x, y := s.uprightTopLeft(w, bw)
	return x + float32(bw)/2, y + float32(w.t.feetOff)
}

// Retângulo do desenho visível, na faixa.
func (s *Stage) spriteOnScreen(w *worker) (l, t, r, b float32) {
	tr := w.t
	visH := float32(s.visH(w))
	if tr.upright {
		bw := w.WalkR[0].Bounds().Dx()
		x, y := s.uprightTopLeft(w, bw)
		y += float32(w.TopRow * tr.px)
		return x, y, x + float32(bw), y + visH
	}
	top := float32(s.thick) - s.height(w) - visH
	ax, ay := s.m.point(w.X-float32(tr.spriteW)/2, top)
	bx, by := s.m.point(w.X+float32(tr.spriteW)/2, top+visH)
	return min(ax, bx), min(ay, by), max(ax, bx), max(ay, by)
}

// ---------- partículas ----------

func (s *Stage) r(a, b float32) float32 { return a + s.rnd.Float32()*(b-a) }

func (s *Stage) pick(list ...string) string { return list[s.rnd.Intn(len(list))] }

// Efeitos em coordenadas do desenho: origem nos pés, x para onde ele olha (já multiplicado por f), y para cima negativo.
func (s *Stage) spawn(effect string, w *worker, count int) {
	if effect == "" {
		return
	}
	if strings.Contains(effect, "+") {
		for _, part := range strings.Split(effect, "+") {
			s.spawn(strings.TrimSpace(part), w, count)
		}
		return
	}
	t := w.t
	sc := float32(t.scale)
	si := t.scale
	f := float32(s.face(w))
	sw := float32(t.spriteW)
	frontX, headX := f*sw*0.45, f*sw*0.2
	headY := -float32(s.visH(w))
	midY := -float32(t.ch.FeetRow-w.TopRow) * float32(t.px) * 0.3
	n := func(def int) int {
		if count > 0 {
			return count
		}
		return def
	}
	switch strings.ToLower(effect) {
	case "faiscas":
		for i := 0; i < n(7); i++ {
			s.add(w, frontX, -sc*2, s.r(-110, 110), s.r(-190, -60), 420, s.r(0.35, 0.7), s.pick("#ffd700", "#ffa500", "#ffffff", "#ff4500"), "", si)
		}
	case "poeira":
		for i := 0; i < n(4); i++ {
			s.add(w, s.r(-sw*0.3, sw*0.3), -sc, s.r(-25, 25), s.r(-35, -10), 0, s.r(0.4, 0.8), s.pick("#beaa8c", "#96826e"), "", si*(1+s.rnd.Intn(2)))
		}
	case "terra":
		for i := 0; i < n(6); i++ {
			s.add(w, frontX, -sc*2, s.r(-40, 90)*-f, s.r(-170, -80), 420, s.r(0.4, 0.8), s.pick("#79553a", "#5d402d", "#966e46"), "", si*(1+s.rnd.Intn(2)))
		}
	case "neve":
		for i := 0; i < n(5); i++ {
			s.add(w, frontX+s.r(-10, 10), midY, s.r(-30, 30), s.r(-60, -20), 60, s.r(0.6, 1.1), s.pick("#ffffff", "#b4dcff"), "", si)
		}
	case "estrelas":
		for i := 0; i < n(3); i++ {
			s.add(w, frontX+s.r(-6, 6), midY, s.r(-40, 40), s.r(-80, -30), 60, s.r(0.5, 0.9), s.pick("#ffd700", "#fff096", "#ff69b4"), "", si)
		}
	case "magia":
		for i := 0; i < n(2); i++ {
			s.add(w, headX+s.r(-10, 10), headY+s.r(0, 12), s.r(-24, 24), s.r(-40, -20), 0, s.r(1.0, 1.5),
				s.pick("#b388ff", "#64ffda", "#ffd700"), s.pick("✦", "✧", "★", "✶"), 0)
		}
	case "zzz":
		z := "z"
		if s.rnd.Intn(3) == 0 {
			z = "Z"
		}
		s.add(w, f*sw*0.3, headY+sc*4, f*s.r(8, 16), s.r(-26, -18), 0, 2.2, "#5a6ec8", z, 0)
	case "notas":
		s.add(w, headX, headY, f*s.r(10, 22), s.r(-30, -20), 0, 1.8, s.pick("#783ca0", "#28783c", "#c85028"), s.pick("♪", "♫"), 0)
	case "fumaca":
		for i := 0; i < n(3); i++ {
			if p := s.add(w, headX+s.r(-6, 6), headY+s.r(0, 8), s.r(-10, 10), s.r(-30, -16), 0, s.r(1.0, 1.6), s.pick("#787878", "#969696", "#5f5f5f"), "", si); p != nil {
				p.Grow = sc * 1.8
			}
		}
	case "moedas":
		for i := 0; i < n(2); i++ {
			s.add(w, frontX, midY, s.r(-50, 50), s.r(-190, -120), 420, s.r(0.6, 0.9), s.pick("#ffd700", "#ffd54f"), "", si*2)
		}
		if s.rnd.Intn(3) == 0 {
			s.add(w, headX, headY, f*10, -30, 0, 1.2, "#ffd700", "★", 0)
		}
	case "bugs":
		for i := 0; i < n(1+s.rnd.Intn(2)); i++ {
			if p := s.add(w, frontX, midY, s.r(-60, 60), s.r(-150, -90), 420, s.r(1.6, 2.4), "#e53935", "", si*2); p != nil {
				p.Bug, p.Crawl = true, 38*sc/3
			}
		}
	case "latidos":
		s.add(w, headX+f*sw*0.25, headY+sc*2, f*s.r(10, 20), s.r(-26, -16), 0, 1.3, s.pick("#ffb74d", "#fff176"), s.pick("au!", "au au!", "auu!"), 0)
	case "coracoes":
		s.add(w, headX, headY, f*s.r(6, 16), s.r(-30, -18), 0, 1.6, s.pick("#ec407a", "#f48fb1", "#e53935"), "♥", 0)
	case "fogo":
		// labareda saindo da boca, para a frente
		for i := 0; i < n(12); i++ {
			s.add(w, f*sw*0.5, headY+s.r(2, 4)*sc, f*s.r(140, 240), s.r(-25, 10), -40, s.r(0.25, 0.55),
				s.pick("#ffeb3b", "#ff9800", "#f44336", "#fff3b0"), "", si*(1+s.rnd.Intn(2)))
		}
	case "flechas":
		arrow := "←"
		if f > 0 {
			arrow = "→"
		}
		s.add(w, f*sw*0.5, midY-sc*3, f*s.r(240, 300), s.r(-8, -2), 0, 0.8, "#8d6e63", arrow, 0)
	case "flores":
		for i := 0; i < n(2); i++ {
			s.add(w, frontX+s.r(-6, 6), midY, s.r(-30, 30), s.r(-60, -30), 20, s.r(1.0, 1.5),
				s.pick("#ec407a", "#ffeb3b", "#ab47bc", "#66bb6a"), s.pick("✿", "❀", "✾"), 0)
		}
	}
}

// Cria a partícula convertendo das coordenadas do desenho para a faixa.
func (s *Stage) add(w *worker, ox, oy, vx, vy, grav, life float32, hex, txt string, size int) *particle {
	if len(s.parts) > 300 {
		return nil
	}
	p := &particle{}
	if w.t.upright {
		fx, fy := s.feetOnScreen(w)
		p.X, p.Y, p.VX, p.VY, p.GY = fx+ox, fy+oy, vx, vy, grav
	} else {
		p.X, p.Y = s.m.point(w.X+ox, float32(s.thick)-s.height(w)+oy)
		p.VX, p.VY = s.m.vec(vx, vy)
		p.GX, p.GY = s.m.vec(0, grav)
	}
	// no chão as coisas quicam; no alto e nas laterais elas caem para fora da faixa
	p.Floor = s.zone == ZoneGround
	p.Life, p.MaxLife = life, life
	p.C, p.Text, p.Size, p.Face = col(hex), txt, size, w.t.symbolFace
	s.parts = append(s.parts, p)
	return p
}

func (s *Stage) updateParticles(dt float32) {
	floor := float32(s.b.H)
	kept := s.parts[:0]
	for _, p := range s.parts {
		p.Life -= dt
		if p.Life <= 0 || p.X < -60 || p.Y < -60 || p.X > float32(s.b.W)+60 || p.Y > float32(s.b.H)+60 {
			continue
		}
		p.VX += p.GX * dt
		p.VY += p.GY * dt
		p.X += p.VX * dt
		p.Y += p.VY * dt
		if p.Floor && p.GY > 0 && p.Y > floor-float32(p.Size) {
			p.Y = floor - float32(p.Size)
			if p.Bug {
				p.VY, p.GY = 0, 0
				d := float32(1)
				if p.VX < 0 {
					d = -1
				}
				p.VX = d * p.Crawl
			} else {
				p.VY *= -0.3
				p.VX *= 0.6
			}
		}
		kept = append(kept, p)
	}
	s.parts = kept
}

// ---------- desenho ----------

// Símbolos que a fonte em pixel não tem viram desenhinhos (5×5); "O" é o miolo amarelo da flor.
var symbolSprites = map[string][]string{
	"♥": {".X.X.", "XXXXX", "XXXXX", ".XXX.", "..X.."},
	"♪": {"..XX.", "..X.X", "..X..", "XXX..", "XX..."},
	"♫": {".XXXX", ".X..X", ".X..X", "XX.XX", "XX.XX"},
	"★": {"..X..", ".XXX.", "XXXXX", ".XXX.", ".X.X."},
	"✦": {"..X..", ".XXX.", "XXXXX", ".XXX.", "..X.."},
	"✧": {"..X..", ".X.X.", "X...X", ".X.X.", "..X.."},
	"✶": {"X.X.X", ".XXX.", "XXXXX", ".XXX.", "X.X.X"},
	"✿": {".X.X.", "XXXXX", ".XOX.", "XXXXX", ".X.X."},
	"❀": {"..X..", ".XOX.", "XOOOX", ".XOX.", "..X.."},
	"✾": {"X...X", ".XXX.", ".XOX.", ".XXX.", "X...X"},
	"→": {".....", "...X.", "XXXXX", "...X.", "....."},
	"←": {".....", ".X...", "XXXXX", ".X...", "....."},
}

var (
	bubbleBorder = col("#282830")
	bubbleText   = col("#222228")
	bugHead      = col("#212121")
	flowerCenter = col("#ffeb3b")
)

func (s *Stage) Draw(screen *ebiten.Image) {
	ox, oy := float64(s.b.X), float64(s.b.Y)
	mg := s.m.geoM()
	for _, w := range s.workers {
		if !visible(w) {
			continue
		}
		t := w.t
		flapping := t.ch.Flies() && w.State == sIdle
		right := s.face(w) > 0
		var set []*ebiten.Image
		switch {
		case w.State == sWork:
			set = pickSet(right, w.WorkR, w.WorkL)
		case w.State == sWalk || flapping:
			set = pickSet(right, w.WalkR, w.WalkL)
		default:
			set = pickSet(right, w.IdleR, w.IdleL)
		}
		if len(set) == 0 {
			continue
		}
		frame := w.Frame % len(set)
		if w.State == sJump {
			frame = 0
		}
		bmp := set[frame]
		bw, bh := bmp.Bounds().Dx(), bmp.Bounds().Dy()
		op := &ebiten.DrawImageOptions{}
		if t.upright {
			x, y := s.uprightTopLeft(w, bw)
			op.GeoM.Translate(float64(int(x))+ox, float64(int(y))+oy)
			screen.DrawImage(bmp, op)
			continue
		}
		x := int(w.X) - bw/2
		y := s.thick - int(s.height(w)) - t.feetOff
		if w.Squash > 0 {
			// amassadinho ao aterrissar
			dh := max(t.scale, bh/10)
			op.GeoM.Scale(float64(bw+dh)/float64(bw), float64(bh-dh)/float64(bh))
			op.GeoM.Translate(float64(x-dh/2), float64(y+dh))
		} else {
			op.GeoM.Translate(float64(x), float64(y))
		}
		op.GeoM.Concat(mg)
		op.GeoM.Translate(ox, oy)
		screen.DrawImage(bmp, op)
		if w.Carrying && w.State == sWalk && w.CarryR != nil {
			c := w.CarryL
			if right {
				c = w.CarryR
			}
			bob := (frame % 2) * t.scale
			head := s.thick - int(s.height(w)) - s.visH(w)
			cop := &ebiten.DrawImageOptions{}
			cop.GeoM.Translate(float64(int(w.X)-c.Bounds().Dx()/2), float64(head-c.Bounds().Dy()-t.scale+bob))
			cop.GeoM.Concat(mg)
			cop.GeoM.Translate(ox, oy)
			screen.DrawImage(c, cop)
		}
	}

	for _, p := range s.parts {
		px, py := p.X+float32(ox), p.Y+float32(oy)
		if p.Text != "" {
			if spr, ok := symbolSprites[p.Text]; ok {
				u := float32(2)
				if p.Face != nil {
					u = float32(max(1, int(p.Face.Metrics().HAscent/5)))
				}
				for yy, row := range spr {
					for xx, ch := range row {
						if ch == '.' {
							continue
						}
						c := p.C
						if ch == 'O' {
							c = flowerCenter
						}
						vector.FillRect(screen, px+float32(xx)*u, py+float32(yy)*u, u, u, c, false)
					}
				}
			} else {
				drawText(screen, p.Text, p.Face, float64(px), float64(py), p.C)
			}
			continue
		}
		size := float32(p.Size)
		if p.Grow > 0 {
			size += float32(int(p.Grow * (1 - p.Life/p.MaxLife)))
		}
		vector.FillRect(screen, float32(int(px)), float32(int(py)), size, size, p.C, false)
		if p.Bug {
			// cabeça e perninhas do besouro
			sc := max(1, size/2)
			hx := float32(int(px)) + size
			if p.VX < 0 {
				hx = float32(int(px)) - sc
			}
			vector.FillRect(screen, hx, float32(int(py)), sc, sc, bugHead, false)
			vector.FillRect(screen, float32(int(px))+size/2-sc/2, float32(int(py)), max(1, sc/2), size, bugHead, false)
		}
	}

	if s.bubbles() {
		for _, w := range s.workers {
			if w.Bubble != "" && visible(w) {
				s.drawBubble(screen, w)
			}
		}
	}
}

func pickSet(right bool, r, l []*ebiten.Image) []*ebiten.Image {
	if right {
		return r
	}
	return l
}

func (s *Stage) drawBubble(screen *ebiten.Image, w *worker) {
	tw, th := measure(w.Bubble, s.bubbleFace)
	pw, ph := float32(7*s.dev), float32(3*s.dev)
	bw, bh := float32(tw)+pw*2, float32(th)+ph*2
	l, t, r, b := s.spriteOnScreen(w)
	below := s.zone == ZoneTop // no alto, o balão fica embaixo do bichinho
	carry := float32(0)
	if w.Carrying && w.CarryR != nil {
		carry = float32(w.CarryR.Bounds().Dy() + w.t.scale)
	}
	var by float32
	if below {
		by = min(float32(s.b.H)-bh-1, b+carry+7)
	} else {
		by = max(0, t-carry-bh-7)
	}
	cx := (l + r) / 2
	bx := max(1, min(float32(s.b.W)-bw-1, cx-bw/2))
	ox, oy := float32(s.b.X), float32(s.b.Y)
	bx, by = float32(int(bx+ox)), float32(int(by+oy))
	cx += ox

	vector.FillRect(screen, bx, by, bw, bh, bubbleBorder, false)
	vector.FillRect(screen, bx+1, by+1, bw-2, bh-2, color.White, false)
	// rabinho apontando para o bichinho
	tx := max(bx+4, min(bx+bw-10, cx-3))
	for i := 0; i < 5; i++ {
		ww := float32(7 - i*2 + 2)
		if ww <= 0 {
			break
		}
		ty := by + bh - 1 + float32(i)
		if below {
			ty = by - float32(i)
		}
		vector.FillRect(screen, tx+float32(i), ty, max(1, ww), 1, bubbleBorder, false)
		if ww > 2 {
			vector.FillRect(screen, tx+float32(i)+1, ty, ww-2, 1, color.White, false)
		}
	}
	drawText(screen, w.Bubble, s.bubbleFace, float64(bx+pw), float64(by+ph), bubbleText)
}
