package main

import "math"

// A configuração (config.json), no mesmo formato do programa do Windows (src/Data.cs, classes Config e Group).

const (
	maxCount = 10
	maxSize  = 5

	placeGround = "chao"
	placeTop    = "topo"
	placeSides  = "laterais"
	placeLeft   = "esquerda"
	placeRight  = "direita"
)

var places = []string{placeGround, placeTop, placeSides, placeLeft, placeRight}

// Uma "turma": um tipo de bichinho, quantos, onde e de que tamanho.
type Group struct {
	Character string
	Skin      string // "*" = misturar as cores
	Place     string
	Count     int
	Size      int // pixels de tela por ponto do desenho (antes do ajuste de HiDPI)
}

type Config struct {
	Enabled        bool // os bichinhos estão na tela
	Bubbles        bool // falas em balõezinhos
	HideFullscreen bool // somem em vídeos, jogos e apresentações em tela cheia
	Speed          float64
	Groups         []*Group
}

// A turma de quem acabou de instalar: um de cada tipo, para mostrar o que dá para fazer.
func defaultConfig() *Config {
	return &Config{Enabled: true, Bubbles: true, HideFullscreen: true, Speed: 1, Groups: []*Group{
		{Character: "gatos", Skin: "*", Place: placeGround, Count: 2, Size: 2},
		{Character: "lagartixa", Skin: "*", Place: placeRight, Count: 1, Size: 2},
		{Character: "borboleta", Skin: "*", Place: placeTop, Count: 1, Size: 2},
	}}
}

func loadConfig() *Config { return loadConfigFrom(configFile()) }

func loadConfigFrom(path string) *Config {
	c := defaultConfig()
	d, err := LoadObj(path)
	if err != nil {
		logf("config inválida: %v", err)
		return c
	}
	c.Enabled = jBool(d, "ativo", c.Enabled)
	c.Bubbles = jBool(d, "baloes", c.Bubbles)
	c.HideFullscreen = jBool(d, "esconderTelaCheia", c.HideFullscreen)
	c.Speed = math.Max(0.3, math.Min(3, jNum(d, "velocidade", c.Speed)))
	if _, ok := d.Get("turmas"); ok {
		c.Groups = nil
		for _, v := range jArr(d, "turmas") {
			o, ok := v.(*Obj)
			if !ok {
				continue
			}
			g := &Group{Character: jStr(o, "personagem", "gatos"), Skin: jStr(o, "cor", "*"), Place: jStr(o, "lugar", placeGround)}
			if indexOf(places, g.Place) < 0 {
				g.Place = placeGround
			}
			g.Count = max(1, min(maxCount, int(jNum(o, "quantidade", 2))))
			g.Size = max(1, min(maxSize, int(jNum(o, "tamanho", 2))))
			c.Groups = append(c.Groups, g)
		}
	}
	return c
}

func (c *Config) Save() error {
	d := NewObj()
	d.Set("ativo", c.Enabled)
	d.Set("baloes", c.Bubbles)
	d.Set("esconderTelaCheia", c.HideFullscreen)
	d.Set("velocidade", c.Speed)
	var groups []any
	for _, g := range c.Groups {
		o := NewObj()
		o.Set("personagem", g.Character)
		o.Set("cor", g.Skin)
		o.Set("lugar", g.Place)
		o.Set("quantidade", g.Count)
		o.Set("tamanho", g.Size)
		groups = append(groups, o)
	}
	if groups == nil {
		groups = []any{}
	}
	d.Set("turmas", groups)
	return SaveJSON(configFile(), d)
}

func defaultPlace(typ string) string {
	switch typ {
	case Climber:
		return placeSides
	case Flyer:
		return placeTop
	}
	return placeGround
}

// Onde cada tipo pode ficar: terrestres só no chão; escaladores e voadores dividem o alto e as laterais.
func allows(typ, place string) bool {
	if typ == Climber || typ == Flyer {
		return place != placeGround
	}
	return place == placeGround
}

// O lugar pedido, se o tipo puder ficar lá; senão, o lugar natural dele.
func placeFor(typ, place string) string {
	if allows(typ, place) {
		return place
	}
	return defaultPlace(typ)
}

func indexOf(list []string, v string) int {
	for i, x := range list {
		if x == v {
			return i
		}
	}
	return -1
}
