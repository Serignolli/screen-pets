package main

import (
	"image"
	"image/color"
	"image/png"
	"math"
	"math/rand"
	"os"
	"time"

	"github.com/hajimehoshi/ebiten/v2"
	"github.com/hajimehoshi/ebiten/v2/vector"
)

// O programa rodando (port de src/Host.cs). No Windows cada borda é uma janela; aqui o Ebitengine só tem uma,
// então ela cobre a área de trabalho inteira (transparente, sem receber cliques) e cada borda é um Stage dentro dela.
// Não há ícone perto do relógio: a configuração é o comando "screenpets", que grava o config.json, e este
// programa percebe a mudança e refaz as turmas na hora.
type Host struct {
	cfg        *Config
	cfgStamp   time.Time
	chars      []*Character
	stages     []*Stage
	rnd        *rand.Rand
	dev        float64
	wa         rect // área de trabalho, em pontos da tela
	winW, winH int  // tamanho da janela em pixels
	last       time.Time
	nextCheck  time.Time
	ready      bool
}

func runPets() {
	lock, ok := acquireLock("bichinhos")
	if !ok {
		return // já estão na tela
	}
	defer lock.Release()
	h := &Host{rnd: rand.New(rand.NewSource(time.Now().UnixNano()))}
	h.cfg = loadConfig()
	h.cfgStamp = stamp(configFile())
	if !h.cfg.Enabled {
		return
	}
	var errs []string
	h.chars = loadAllCharacters(&errs)
	for _, e := range errs {
		logf("personagem com erro: %s", e)
	}
	ebiten.SetWindowDecorated(false)
	ebiten.SetWindowFloating(true)
	ebiten.SetWindowMousePassthrough(true)
	ebiten.SetWindowResizingMode(ebiten.WindowResizingModeDisabled)
	ebiten.SetWindowTitle("Screen Pets")
	ebiten.SetRunnableOnUnfocused(true)
	ebiten.SetTPS(33)
	ebiten.SetWindowSize(1, 1)
	ebiten.SetWindowPosition(-10000, -10000)
	err := ebiten.RunGameWithOptions(h, &ebiten.RunGameOptions{ScreenTransparent: transparent(), InitUnfocused: true, SkipTaskbar: true,
		X11ClassName: "ScreenPets", X11InstanceName: "screenpets"})
	if err != nil && err != ebiten.Termination {
		// sem janela (sem vídeo, sem OpenGL...): não adianta tentar de novo
		logf("bichinhos: %v", err)
	}
}

// SCREENPETS_OPACO=1 abre a janela sem transparência (para testar onde não há OpenGL, como no Windows).
func transparent() bool { return os.Getenv("SCREENPETS_OPACO") == "" }

func (h *Host) setup() {
	h.dev = ebiten.Monitor().DeviceScaleFactor()
	if h.dev <= 0 {
		h.dev = 1
	}
	macAccessory()
	watchFullscreen()
	h.rebuild()
	h.applyArea(workArea())
	mw, mh := ebiten.Monitor().Size()
	logf("tela %dx%d, escala %.2f, área de trabalho %+v", mw, mh, h.dev, h.wa)
	h.ready = true
}

// A janela ocupa a área de trabalho (sem o Dock, a barra de menus ou os painéis do sistema).
func (h *Host) applyArea(wa rect) {
	h.wa = wa
	ebiten.SetWindowSize(wa.W, wa.H)
	ebiten.SetWindowPosition(wa.X, wa.Y)
	h.winW = int(math.Ceil(float64(wa.W) * h.dev))
	h.winH = int(math.Ceil(float64(wa.H) * h.dev))
	for _, s := range h.stages {
		s.place(rect{0, 0, h.winW, h.winH})
	}
}

func (h *Host) rebuild() {
	h.stages = nil
	for _, zc := range castsByZone(h.cfg, h.chars) {
		s := newStage(zc.zone, zc.casts, h.cfg, h.dev, h.rnd)
		if h.winW > 0 {
			s.place(rect{0, 0, h.winW, h.winH})
		}
		h.stages = append(h.stages, s)
	}
}

type zoneCasts struct {
	zone  Zone
	casts []Cast
}

// Quem vai em cada borda. "Nas duas laterais" divide a turma entre a direita e a esquerda.
func castsByZone(cfg *Config, chars []*Character) []zoneCasts {
	var out []zoneCasts
	add := func(z Zone, ch *Character, g *Group, n int) {
		if n <= 0 {
			return
		}
		for i := range out {
			if out[i].zone == z {
				out[i].casts = append(out[i].casts, Cast{ch, g.Skin, n, g.Size})
				return
			}
		}
		out = append(out, zoneCasts{z, []Cast{{ch, g.Skin, n, g.Size}}})
	}
	for _, g := range cfg.Groups {
		ch := findCharacter(chars, g.Character)
		if ch == nil {
			continue
		}
		switch placeFor(ch.Type, g.Place) {
		case placeSides:
			add(ZoneRight, ch, g, (g.Count+1)/2)
			add(ZoneLeft, ch, g, g.Count/2)
		case placeTop:
			add(ZoneTop, ch, g, g.Count)
		case placeLeft:
			add(ZoneLeft, ch, g, g.Count)
		case placeRight:
			add(ZoneRight, ch, g, g.Count)
		default:
			add(ZoneGround, ch, g, g.Count)
		}
	}
	return out
}

func (h *Host) Update() error {
	now := time.Now()
	if !h.ready {
		h.setup()
		h.last = now
		return nil
	}
	dt := float32(math.Max(0, math.Min(0.1, now.Sub(h.last).Seconds())))
	h.last = now
	if now.After(h.nextCheck) {
		h.nextCheck = now.Add(700 * time.Millisecond)
		if st := stamp(configFile()); !st.Equal(h.cfgStamp) {
			h.cfgStamp = st
			h.cfg = loadConfig()
			if !h.cfg.Enabled {
				return ebiten.Termination
			}
			h.rebuild()
		}
		if len(h.stages) == 0 {
			return ebiten.Termination
		}
		if wa := workArea(); wa != h.wa {
			h.applyArea(wa)
		}
	}
	if h.hidden() {
		return nil
	}
	for _, s := range h.stages {
		s.Step(dt)
	}
	return nil
}

// Some quando um vídeo, jogo ou apresentação ocupa a tela inteira.
func (h *Host) hidden() bool { return h.cfg.HideFullscreen && fullscreenOn() }

func (h *Host) Draw(screen *ebiten.Image) {
	if !h.ready || h.hidden() {
		return
	}
	for _, s := range h.stages {
		s.Draw(screen)
	}
}

func (h *Host) Layout(ow, oh int) (int, int) {
	if !h.ready {
		return max(1, ow), max(1, oh)
	}
	return h.winW, h.winH
}

// ---------- foto: simula e desenha numa tela de mentira, sem abrir a janela dos bichinhos ----------

type photo struct {
	secs     float64
	out, cfg string
	err      error
}

// foto <segundos> <arquivo.png> [config.json]: como o "foto" do programa do Windows, para conferir o port.
func runPhoto(secs float64, out, cfg string) {
	p := &photo{secs: secs, out: out, cfg: cfg}
	ebiten.SetWindowDecorated(false)
	ebiten.SetWindowSize(1, 1)
	ebiten.SetWindowPosition(-10000, -10000)
	if err := ebiten.RunGameWithOptions(p, &ebiten.RunGameOptions{InitUnfocused: true, SkipTaskbar: true}); err != nil && err != ebiten.Termination {
		logf("foto: %v", err)
	}
	if p.err != nil {
		logf("foto: %v", p.err)
		os.Exit(1)
	}
}

func (p *photo) Update() error {
	const sw, sh, bar = 1600, 900, 40
	cfg := loadConfigFrom(p.cfg)
	chars := loadAllCharacters(nil)
	rnd := rand.New(rand.NewSource(1))
	img := ebiten.NewImage(sw, sh)
	img.Fill(color.RGBA{120, 144, 156, 255})
	vector.FillRect(img, 0, sh-bar, sw, bar, color.RGBA{32, 32, 36, 255}, false)
	for _, zc := range castsByZone(cfg, chars) {
		s := newStage(zc.zone, zc.casts, cfg, 1, rnd)
		s.place(rect{0, 0, sw, sh - bar})
		for t := 0.0; t < p.secs; t += 0.03 {
			s.Step(0.03)
		}
		s.Draw(img)
		vector.StrokeRect(img, float32(s.b.X)+0.5, float32(s.b.Y)+0.5, float32(s.b.W-1), float32(s.b.H-1), 1, color.RGBA{255, 255, 255, 90}, false)
	}
	rgba := image.NewRGBA(img.Bounds())
	img.ReadPixels(rgba.Pix)
	f, err := os.Create(p.out)
	if err != nil {
		p.err = err
		return ebiten.Termination
	}
	defer f.Close()
	p.err = png.Encode(f, rgba)
	return ebiten.Termination
}

func (p *photo) Draw(*ebiten.Image)         {}
func (p *photo) Layout(w, h int) (int, int) { return max(1, w), max(1, h) }
