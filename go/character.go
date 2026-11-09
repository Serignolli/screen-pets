package main

import (
	"errors"
	"fmt"
	"image"
	"image/color"
	"math"
	"path/filepath"
	"sort"
	"strconv"
	"strings"
)

// Personagens em JSON (personagens/*.json): desenho em texto, cada letra é uma cor da skin, "." é transparente.
// Mesma leitura do programa do Windows (src/Data.cs, classe Character).

// Os três tipos de bichinho.
const (
	Walker  = "terrestre"
	Climber = "escalador"
	Flyer   = "voador"
)

type Frames map[string][][]string // tipo de quadro -> quadros -> linhas

type Skin struct {
	ID, Name string
	Colors   map[rune]color.RGBA
	Frames   Frames
	Effect   *string
	Carry    *string
	Base     string
	TopRow   int
}

type Character struct {
	ID, Name, Type, Description, Effect, Carry, Source string
	Frames                                             Frames
	Skins                                              []*Skin
	Speech                                             []string
	WorkEffects                                        map[string]string
	Width, Height, TopRow, BottomRow                   int
	FeetRow                                            int // linha dos pés (o fim da pose parada): é o que encosta no chão (ou na parede)
	Detail                                             int
	FlyHeight                                          int     // "voo": altura em pontos do desenho acima do chão (0 = anda)
	Pace                                               float64 // "ritmo": o caracol é devagar, o passarinho é rápido
}

func (c *Character) Flies() bool { return c.FlyHeight > 0 }

func blank(c rune) bool { return c == '.' || c == ' ' }

func readSprites(o *Obj) (Frames, error) {
	result := Frames{}
	for _, k := range o.Keys() {
		v, _ := o.Get(k)
		list, ok := v.([]any)
		if !ok {
			continue
		}
		var frames [][]string
		for _, f := range list {
			rows, ok := f.([]any)
			if !ok {
				continue
			}
			var fr []string
			for _, r := range rows {
				s, _ := r.(string)
				fr = append(fr, s)
			}
			if len(fr) > 0 {
				frames = append(frames, fr)
			}
		}
		if len(frames) > 0 {
			result[k] = frames
		}
	}
	if len(result) == 0 {
		return result, nil
	}
	if _, ok := result["andar"]; !ok {
		return nil, errors.New(`faltou "sprites.andar"`)
	}
	if _, ok := result["trabalhar"]; !ok {
		result["trabalhar"] = result["andar"]
	}
	return result, nil
}

func parseColor(s string) color.RGBA {
	s = strings.TrimSpace(s)
	c := color.RGBA{128, 128, 128, 255}
	if strings.HasPrefix(s, "#") {
		h := s[1:]
		if len(h) == 3 {
			h = string([]byte{h[0], h[0], h[1], h[1], h[2], h[2]})
		}
		if len(h) == 8 {
			h = h[2:]
		}
		if v, err := strconv.ParseUint(h, 16, 32); err == nil && len(h) == 6 {
			c = color.RGBA{uint8(v >> 16), uint8(v >> 8), uint8(v), 255}
		}
	} else if named, ok := namedColors[strings.ToLower(s)]; ok {
		c = named
	}
	return c
}

var namedColors = map[string]color.RGBA{
	"white": {255, 255, 255, 255}, "black": {0, 0, 0, 255}, "gold": {255, 215, 0, 255}, "orange": {255, 165, 0, 255},
	"red": {255, 0, 0, 255}, "gray": {128, 128, 128, 255}, "orangered": {255, 69, 0, 255}, "hotpink": {255, 105, 180, 255},
}

var colorCache = map[string]color.RGBA{}

// cor de "#rrggbb" com cache (as salas pedem as mesmas cores toda hora)
func col(hex string) color.RGBA {
	if c, ok := colorCache[hex]; ok {
		return c
	}
	c := parseColor(hex)
	colorCache[hex] = c
	return c
}

func loadCharacterFile(path string) (*Character, error) {
	o, err := LoadObj(path)
	if err != nil {
		return nil, err
	}
	return loadCharacter(o, path)
}

func loadCharacter(d *Obj, path string) (*Character, error) {
	c := &Character{Source: path, WorkEffects: map[string]string{}}
	c.ID = jStr(d, "id", strings.TrimSuffix(filepath.Base(path), filepath.Ext(path)))
	c.Name = jStr(d, "nome", c.ID)
	c.Description = jStr(d, "descricao", "")
	c.Effect = jStr(d, "efeito", "")
	c.Carry = jStr(d, "carga", "")
	c.Detail = max(1, min(8, int(jNum(d, "detalhe", 1))))
	c.FlyHeight = max(0, min(64, int(jNum(d, "voo", 0))))
	c.Pace = math.Max(0.1, math.Min(4, jNum(d, "ritmo", 1)))
	def := Walker
	if c.FlyHeight > 0 {
		def = Flyer
	}
	c.Type = jStr(d, "tipo", def)
	if c.Type != Walker && c.Type != Climber && c.Type != Flyer {
		c.Type = Walker
	}
	c.Speech, _ = jStrs(d, "falas")
	if fx := jObj(d, "efeitosTrabalho"); fx != nil {
		for _, k := range fx.Keys() {
			c.WorkEffects[k] = jStr(fx, k, "")
		}
	}
	if sp := jObj(d, "sprites"); sp != nil {
		f, err := readSprites(sp)
		if err != nil {
			return nil, err
		}
		c.Frames = f
	}
	for i, so := range jArr(d, "skins") {
		sd, ok := so.(*Obj)
		if !ok {
			continue
		}
		s := &Skin{Colors: map[rune]color.RGBA{}}
		s.ID = jStr(sd, "id", fmt.Sprintf("skin%d", i))
		s.Name = jStr(sd, "nome", s.ID)
		if v, ok := sd.Get("efeito"); ok {
			if str, ok := v.(string); ok {
				s.Effect = &str
			}
		}
		if v, ok := sd.Get("carga"); ok {
			if str, ok := v.(string); ok {
				s.Carry = &str
			}
		}
		s.Base = jStr(sd, "base", "")
		if cores := jObj(sd, "cores"); cores != nil {
			for _, k := range cores.Keys() {
				if r := []rune(k); len(r) > 0 {
					s.Colors[r[0]] = parseColor(jStr(cores, k, ""))
				}
			}
		}
		if own := jObj(sd, "sprites"); own != nil {
			f, err := readSprites(own)
			if err != nil {
				return nil, err
			}
			if len(f) > 0 {
				s.Frames = f
			}
		}
		c.Skins = append(c.Skins, s)
	}
	if len(c.Skins) == 0 {
		return nil, errors.New("precisa de pelo menos uma skin")
	}
	// "base": a skin reaproveita desenho, cores, efeito e carga de outra
	byID := map[string]*Skin{}
	for _, s := range c.Skins {
		byID[s.ID] = s
	}
	for _, s := range c.Skins {
		seen := map[*Skin]bool{s: true}
		for b := s.Base; b != ""; {
			bs := byID[b]
			if bs == nil || seen[bs] {
				break
			}
			seen[bs] = true
			if s.Frames == nil {
				s.Frames = bs.Frames
			}
			for k, v := range bs.Colors {
				if _, ok := s.Colors[k]; !ok {
					s.Colors[k] = v
				}
			}
			if s.Effect == nil {
				s.Effect = bs.Effect
			}
			if s.Carry == nil {
				s.Carry = bs.Carry
			}
			b = bs.Base
		}
	}
	if len(c.Frames) == 0 {
		for _, s := range c.Skins {
			if s.Frames != nil {
				c.Frames = s.Frames
				break
			}
		}
		if len(c.Frames) == 0 {
			return nil, errors.New(`faltou "sprites"`)
		}
	}
	for _, s := range c.Skins {
		if s.Frames == nil {
			s.Frames = c.Frames
		}
	}

	// Normaliza: todos os quadros com a mesma largura/altura, alinhados por baixo.
	sets := []Frames{c.Frames}
	for _, s := range c.Skins {
		dup := false
		for _, x := range sets {
			if sameFrames(x, s.Frames) {
				dup = true
			}
		}
		if !dup {
			sets = append(sets, s.Frames)
		}
	}
	w, h := 0, 0
	for _, set := range sets {
		for _, l := range set {
			for _, fr := range l {
				h = max(h, len(fr))
				for _, r := range fr {
					w = max(w, len([]rune(r)))
				}
			}
		}
	}
	c.Width, c.Height = w, h
	c.TopRow, c.BottomRow = h, 0
	tops := map[int]int{}
	for si, set := range sets {
		top := h
		for k, frames := range set {
			norm := make([][]string, len(frames))
			for fi, fr := range frames {
				rows := make([]string, h)
				pad := h - len(fr)
				for y := 0; y < h; y++ {
					r := ""
					if y >= pad {
						r = fr[y-pad]
					}
					rr := []rune(r)
					for len(rr) < w {
						rr = append(rr, '.')
					}
					rows[y] = string(rr)
					for _, ch := range rr {
						if !blank(ch) {
							top = min(top, y)
							c.BottomRow = max(c.BottomRow, y)
							break
						}
					}
				}
				norm[fi] = rows
			}
			set[k] = norm
		}
		tops[si] = top
		c.TopRow = min(c.TopRow, top)
	}
	if c.TopRow > c.BottomRow {
		return nil, errors.New("os sprites estão vazios")
	}
	for _, s := range c.Skins {
		for si, set := range sets {
			if sameFrames(set, s.Frames) {
				s.TopRow = min(tops[si], c.BottomRow)
			}
		}
	}
	c.FeetRow = c.BottomRow
	pose, ok := c.Frames["parado"]
	if !ok {
		pose = c.Frames["andar"]
	}
	feet := -1
	for _, fr := range pose {
		for y := len(fr) - 1; y > feet; y-- {
			if strings.IndexFunc(fr[y], func(r rune) bool { return !blank(r) }) >= 0 {
				feet = y
				break
			}
		}
	}
	if feet >= c.TopRow {
		c.FeetRow = feet
	}
	// Skins sem "base" herdam da primeira as cores que não definirem.
	for _, s := range c.Skins[1:] {
		if s.Base == "" {
			for k, v := range c.Skins[0].Colors {
				if _, ok := s.Colors[k]; !ok {
					s.Colors[k] = v
				}
			}
		}
	}
	return c, nil
}

// os mapas de quadros são compartilhados entre skins: compara pela identidade
func sameFrames(a, b Frames) bool {
	if a == nil || b == nil {
		return false
	}
	return fmt.Sprintf("%p", a) == fmt.Sprintf("%p", b)
}

// Terrestres, escaladores e voadores, nesta ordem (a mesma de src/Data.cs).
var characterOrder = []string{
	"gatos", "cachorros", "pinguins", "formigas", "coelhos",
	"lagartixa", "joaninha", "caracol", "aranha", "besouro",
	"passarinho", "borboleta", "abelha", "morcego", "dragao"}

func loadAllCharacters(errs *[]string) []*Character {
	var list []*Character
	files, _ := filepath.Glob(filepath.Join(charactersDir(), "*.json"))
	for _, f := range files {
		c, err := loadCharacterFile(f)
		if err != nil {
			if errs != nil {
				*errs = append(*errs, filepath.Base(f)+": "+err.Error())
			}
			continue
		}
		list = append(list, c)
	}
	rank := func(id string) int {
		for i, o := range characterOrder {
			if o == id {
				return i
			}
		}
		return 99
	}
	sort.SliceStable(list, func(i, j int) bool {
		if ri, rj := rank(list[i].ID), rank(list[j].ID); ri != rj {
			return ri < rj
		}
		return list[i].Name < list[j].Name
	})
	return list
}

func findCharacter(all []*Character, id string) *Character {
	for _, c := range all {
		if c.ID == id {
			return c
		}
	}
	return nil
}

func (c *Character) PixelScale(unit int) int {
	return max(1, int(float64(unit)/float64(c.Detail)+0.5))
}

func (c *Character) GetSkin(id string) *Skin {
	for _, s := range c.Skins {
		if s.ID == id {
			return s
		}
	}
	return c.Skins[0]
}

func (c *Character) EffectOf(s *Skin, kind string) string {
	if e, ok := c.WorkEffects[kind]; ok {
		return e
	}
	if s != nil && s.Effect != nil {
		return *s.Effect
	}
	return c.Effect
}

func (c *Character) CarryOf(s *Skin) string {
	if s != nil && s.Carry != nil {
		return *s.Carry
	}
	return c.Carry
}

// "trabalhar" e quaisquer "trabalhar-algo"
func (c *Character) WorkKinds(s *Skin) []string {
	var l []string
	for k := range s.Frames {
		if k == "trabalhar" || strings.HasPrefix(k, "trabalhar-") {
			l = append(l, k)
		}
	}
	sort.Slice(l, func(i, j int) bool {
		a, b := l[i], l[j]
		if a == "trabalhar" {
			a = ""
		}
		if b == "trabalhar" {
			b = ""
		}
		return a < b
	})
	return l
}

// Quadros de um tipo ("parado" sem desenho próprio usa o primeiro de "andar"; um "trabalhar-algo" que falta vira "trabalhar").
func (c *Character) FramesOf(s *Skin, kind string) [][]string {
	if f, ok := s.Frames[kind]; ok {
		return f
	}
	if strings.HasPrefix(kind, "trabalhar-") {
		if f, ok := s.Frames["trabalhar"]; ok {
			return f
		}
	}
	if kind == "parado" {
		return s.Frames["andar"][:1]
	}
	return s.Frames["andar"]
}

// Render desenha os quadros num *image.RGBA com scale pixels por ponto (flip espelha para a esquerda).
func (c *Character) Render(s *Skin, kind string, scale int, flip bool) []*image.RGBA {
	frames := c.FramesOf(s, kind)
	out := make([]*image.RGBA, len(frames))
	for i, f := range frames {
		out[i] = renderPixels(f, s.Colors, scale, flip)
	}
	return out
}

func renderPixels(rows []string, colors map[rune]color.RGBA, scale int, flip bool) *image.RGBA {
	w, h := 0, len(rows)
	rr := make([][]rune, h)
	for y, r := range rows {
		rr[y] = []rune(r)
		w = max(w, len(rr[y]))
	}
	img := image.NewRGBA(image.Rect(0, 0, max(1, w*scale), max(1, h*scale)))
	for y := 0; y < h; y++ {
		for x, ch := range rr[y] {
			if blank(ch) {
				continue
			}
			cl, ok := colors[ch]
			if !ok {
				cl = color.RGBA{128, 128, 128, 255}
			}
			dx := x
			if flip {
				dx = w - 1 - x
			}
			for py := 0; py < scale; py++ {
				for px := 0; px < scale; px++ {
					img.SetRGBA(dx*scale+px, y*scale+py, cl)
				}
			}
		}
	}
	return img
}

// Objetos que os bichinhos carregam na cabeça (mesmos desenhos de src/Data.cs, classe Prop).
type Prop struct {
	Rows   []string
	Colors map[rune]color.RGBA
}

func makeProp(rows []string, colors ...string) *Prop {
	p := &Prop{Rows: rows, Colors: map[rune]color.RGBA{}}
	for _, c := range colors {
		p.Colors[rune(c[0])] = parseColor(c[2:])
	}
	return p
}

func getProp(name string) *Prop {
	switch strings.ToLower(name) {
	case "chocolate":
		return makeProp([]string{"FFFPPPPP", "BLBLBPPP", "BBBBBPPP"}, "F=#d9d9d9", "P=#7b1fa2", "B=#5b3416", "L=#7d4a22")
	case "caixa":
		return makeProp([]string{"KKKKKKKK", "KCCTTCCK", "KCCTTCCK", "KCCCCCCK", "KKKKKKKK"}, "K=#6d4c2f", "C=#d2a86e", "T=#b98a4e")
	case "peixe":
		return makeProp([]string{"B.BBBB..", "BBBBBKBB", "B.BBBB.."}, "B=#64b5f6", "K=#0d47a1")
	case "acucar":
		return makeProp([]string{"WWWW", "WWWS", "WWWS", "WSSS"}, "W=#ffffff", "S=#cfd8dc")
	case "engrenagem":
		return makeProp([]string{"..G.G..", ".GGGGG.", "GGG.GGG", ".GGGGG.", "..G.G.."}, "G=#90a4ae")
	case "gema":
		return makeProp([]string{".CCC.", "CCWCC", ".CCC.", "..C.."}, "C=#26c6da", "W=#e0f7fa")
	case "folha":
		return makeProp([]string{".GGG.", "GGJGG", ".GGG."}, "G=#43a047", "J=#2e7d32")
	case "claude":
		return makeProp([]string{"...O...", ".O.O.O.", "..OOO..", "OOOCOOO", "..OOO..", ".O.O.O.", "...O..."}, "O=#d97757", "C=#b85f42")
	case "terminal":
		return makeProp([]string{"KKKKKKKKK", "KGKKKKKKK", "KKGKKKKKK", "KGKKGGGKK", "KKKKKKKKK"}, "K=#262624", "G=#f0eee6")
	case "cafe":
		return makeProp([]string{".s..s..", "..s..s.", "RRRRR..", "RWRRRRR", "RWRRR.R", "RRRRRRR", ".RRR..."}, "R=#e53935", "W=#ffcdd2", "s=#cfd8dc")
	case "osso":
		return makeProp([]string{"WW....WW", "WWWWWWWW", "WW....WW"}, "W=#f3ead3")
	case "disquete":
		return makeProp([]string{"KKKKKK", "KSSSKK", "KSSSKK", "KKKKKK", "KWWWWK", "KWWWWK"}, "K=#1e3a8a", "S=#b0bec5", "W=#eceff1")
	case "bola":
		return makeProp([]string{".YYY.", "WYYYY", "YWWYY", "YYYWW", ".YYY."}, "Y=#c6e03a", "W=#f5f5f5")
	case "pizza":
		return makeProp([]string{"OOOOOOO", ".YRYYY.", "..YYR..", "...Y..."}, "O=#c68642", "Y=#ffd54f", "R=#d32f2f")
	case "cenoura":
		return makeProp([]string{"GOOOO...", "GGOOOOOO", "GOOOO..."}, "O=#ff9800", "G=#43a047")
	case "flor":
		return makeProp([]string{".P.P.", "PPYPP", ".PPP.", "..G..", ".GG.."}, "P=#ec407a", "Y=#ffeb3b", "G=#43a047")
	}
	return nil
}
