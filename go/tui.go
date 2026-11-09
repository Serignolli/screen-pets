package main

import (
	"bufio"
	"fmt"
	"os"
	"path/filepath"
	"runtime"
	"strconv"
	"strings"
)

// A configuração no terminal (no Windows é a janela de src/ConfigForm.cs). Tudo vale na hora:
// cada escolha grava o config.json e os bichinhos que estão na tela se refazem sozinhos.

var stdin = bufio.NewReader(os.Stdin)

func ask(prompt string) string {
	fmt.Print(prompt)
	line, err := stdin.ReadString('\n')
	if err != nil && line == "" {
		fmt.Println()
		os.Exit(0) // Ctrl+D ou sem terminal
	}
	return strings.TrimSpace(line)
}

func askYesNo(prompt string, def bool) bool {
	hint := " [S/n] "
	if !def {
		hint = " [s/N] "
	}
	for {
		switch strings.ToLower(ask(prompt + hint)) {
		case "":
			return def
		case "s", "sim", "y", "yes":
			return true
		case "n", "nao", "não", "no":
			return false
		}
	}
}

// Mostra a lista numerada e devolve o índice escolhido (ou def se só apertar Enter).
func choose(title string, options []string, def int) int {
	fmt.Println()
	fmt.Println(title)
	for i, o := range options {
		mark := "  "
		if i == def {
			mark = "▸ "
		}
		fmt.Printf("  %s%2d. %s\n", mark, i+1, o)
	}
	for {
		a := ask(fmt.Sprintf("Número [%d]: ", def+1))
		if a == "" {
			return def
		}
		if n, err := strconv.Atoi(a); err == nil && n >= 1 && n <= len(options) {
			return n - 1
		}
	}
}

func bold(s string) string {
	if runtime.GOOS == "windows" {
		return s
	}
	return "\033[1m" + s + "\033[0m"
}

func onOff(b bool) string {
	if b {
		return "sim"
	}
	return "não"
}

var (
	typeIDs    = []string{Walker, Climber, Flyer}
	typeNames  = []string{"Terrestres (andam embaixo da tela)", "Escaladores (sobem pelas laterais)", "Voadores (voam no alto da tela)"}
	sizeNames  = []string{"Bem pequeno", "Pequeno", "Médio", "Grande", "Enorme"}
	speeds     = []float64{0.5, 0.75, 1, 1.4, 2}
	speedNames = []string{"Devagar", "Calma", "Normal", "Rápida", "Correria"}
)

func placeName(p string) string {
	switch p {
	case placeTop:
		return "No alto da tela"
	case placeSides:
		return "Nas duas laterais"
	case placeLeft:
		return "Na lateral esquerda"
	case placeRight:
		return "Na lateral direita"
	}
	if runtime.GOOS == "darwin" {
		return "Embaixo, em cima do Dock"
	}
	return "Embaixo da tela"
}

func speedIndex(v float64) int {
	best := 2
	for i, s := range speeds {
		if abs(s-v) < abs(speeds[best]-v) {
			best = i
		}
	}
	return best
}

func abs(v float64) float64 {
	if v < 0 {
		return -v
	}
	return v
}

func groupLabel(g *Group, all []*Character) string {
	name, color := g.Character, "misturando as cores"
	if ch := findCharacter(all, g.Character); ch != nil {
		name = ch.Name
		if g.Skin != "*" {
			color = strings.ToLower(ch.GetSkin(g.Skin).Name)
		}
	}
	return fmt.Sprintf("%s · %d · %s · %s · %s", name, g.Count, color, strings.ToLower(placeName(g.Place)), strings.ToLower(sizeNames[g.Size-1]))
}

// Grava e deixa os bichinhos de acordo: abre se for para aparecerem, fecha se não.
func apply(cfg *Config) {
	if err := cfg.Save(); err != nil {
		fmt.Println("Não deu para salvar a configuração: " + err.Error())
		return
	}
	switch {
	case !cfg.Enabled || len(cfg.Groups) == 0:
		killOthers()
	case !isRunning("bichinhos"):
		launchDetached("rodar")
	}
}

func runConfig() {
	all := loadAllCharacters(nil)
	if len(all) == 0 {
		fmt.Println("Não encontrei os bichinhos em " + charactersDir())
		os.Exit(1)
	}
	cfg := loadConfig()
	if !fileExists(configFile()) {
		apply(cfg)
	} else if cfg.Enabled && len(cfg.Groups) > 0 && !isRunning("bichinhos") {
		launchDetached("rodar")
	}
	fmt.Println()
	fmt.Println(bold("Screen Pets "+version) + ": seus bichinhos. Tudo vale na hora.")
	for {
		fmt.Println()
		fmt.Println(bold("Na sua tela:"))
		if len(cfg.Groups) == 0 {
			fmt.Println("  ninguém ainda")
		}
		for i, g := range cfg.Groups {
			fmt.Printf("  %d. %s\n", i+1, groupLabel(g, all))
		}
		show := "Esconder os bichinhos"
		if !cfg.Enabled {
			show = "Mostrar os bichinhos"
		}
		opts := []string{
			"Adicionar bichinho",
			"Mudar uma turma",
			"Tirar uma turma",
			show,
			"Balõezinhos de fala: " + onOff(cfg.Bubbles),
			"Sumir em tela cheia: " + onOff(cfg.HideFullscreen),
			"Velocidade: " + speedNames[speedIndex(cfg.Speed)],
			"Abrir junto com o computador: " + onOff(autostartOn()),
			"Sair (os bichinhos continuam na tela)",
		}
		switch choose(bold("O que você quer fazer?"), opts, len(opts)-1) {
		case 0:
			if g := newGroup(all); g != nil {
				cfg.Groups = append(cfg.Groups, g)
				cfg.Enabled = true
				apply(cfg)
			}
		case 1:
			if i := pickGroup(cfg, all, "Qual turma?"); i >= 0 {
				editGroup(cfg, cfg.Groups[i], all)
			}
		case 2:
			if i := pickGroup(cfg, all, "Tirar qual turma?"); i >= 0 {
				cfg.Groups = append(cfg.Groups[:i], cfg.Groups[i+1:]...)
				apply(cfg)
			}
		case 3:
			cfg.Enabled = !cfg.Enabled
			apply(cfg)
		case 4:
			cfg.Bubbles = !cfg.Bubbles
			apply(cfg)
		case 5:
			cfg.HideFullscreen = !cfg.HideFullscreen
			apply(cfg)
		case 6:
			cfg.Speed = speeds[choose("Velocidade", speedNames, speedIndex(cfg.Speed))]
			apply(cfg)
		case 7:
			if err := setAutostart(!autostartOn()); err != nil {
				fmt.Println("Não deu para mudar: " + err.Error())
			}
		default:
			fmt.Println("Até mais! Para mudar de novo: " + bold("screenpets"))
			return
		}
	}
}

func pickGroup(cfg *Config, all []*Character, title string) int {
	if len(cfg.Groups) == 0 {
		fmt.Println("Ninguém na tela ainda.")
		return -1
	}
	var names []string
	for _, g := range cfg.Groups {
		names = append(names, groupLabel(g, all))
	}
	names = append(names, "Voltar")
	i := choose(title, names, len(names)-1)
	if i == len(cfg.Groups) {
		return -1
	}
	return i
}

func chooseCharacter(all []*Character) *Character {
	kind := typeIDs[choose(bold("Que tipo de bichinho?"), typeNames, 0)]
	var list []*Character
	var names []string
	for _, c := range all {
		if c.Type == kind {
			list = append(list, c)
			names = append(names, c.Name+": "+c.Description)
		}
	}
	if len(list) == 0 {
		return nil
	}
	return list[choose(bold("Qual bichinho?"), names, 0)]
}

func chooseSkin(ch *Character, current string) string {
	names := []string{"Misturar as cores"}
	ids := []string{"*"}
	for _, s := range ch.Skins {
		names = append(names, s.Name)
		ids = append(ids, s.ID)
	}
	return ids[choose("Cor", names, max(0, indexOf(ids, current)))]
}

func choosePlace(ch *Character, current string) string {
	var ids, names []string
	for _, p := range places {
		if allows(ch.Type, p) {
			ids = append(ids, p)
			names = append(names, placeName(p))
		}
	}
	if len(ids) == 1 {
		return ids[0]
	}
	return ids[choose("Onde eles ficam", names, max(0, indexOf(ids, placeFor(ch.Type, current))))]
}

func chooseCount(current int) int {
	for {
		a := ask(fmt.Sprintf("Quantos (1 a %d) [%d]: ", maxCount, current))
		if a == "" {
			return current
		}
		if n, err := strconv.Atoi(a); err == nil && n >= 1 && n <= maxCount {
			return n
		}
	}
}

func chooseSize(current int) int {
	return choose("Tamanho", sizeNames, current-1) + 1
}

func newGroup(all []*Character) *Group {
	ch := chooseCharacter(all)
	if ch == nil {
		return nil
	}
	g := &Group{Character: ch.ID, Skin: "*", Place: defaultPlace(ch.Type), Count: 2, Size: 2}
	if ch.Type != Walker {
		g.Count = 1
	}
	g.Skin = chooseSkin(ch, g.Skin)
	g.Count = chooseCount(g.Count)
	g.Place = choosePlace(ch, g.Place)
	g.Size = chooseSize(g.Size)
	return g
}

func editGroup(cfg *Config, g *Group, all []*Character) {
	for {
		ch := findCharacter(all, g.Character)
		if ch == nil {
			return
		}
		fmt.Println()
		fmt.Println(bold(groupLabel(g, all)))
		opts := []string{"Trocar o bichinho", "Cor", "Quantos", "Onde ficam", "Tamanho", "Voltar"}
		switch choose("Mudar o quê?", opts, len(opts)-1) {
		case 0:
			if c := chooseCharacter(all); c != nil {
				g.Character, g.Skin = c.ID, "*"
				g.Place = placeFor(c.Type, g.Place)
			}
		case 1:
			g.Skin = chooseSkin(ch, g.Skin)
		case 2:
			g.Count = chooseCount(g.Count)
		case 3:
			g.Place = choosePlace(ch, g.Place)
		case 4:
			g.Size = chooseSize(g.Size)
		default:
			return
		}
		apply(cfg)
	}
}

// Tira os bichinhos e apaga a instalação (o atalho em ~/.local/bin, o menu, o "abrir junto", a pasta e o estado).
func uninstallEverything() {
	if !askYesNo("Tirar o Screen Pets e todos os bichinhos deste computador?", false) {
		return
	}
	killOthers()
	_ = setAutostart(false)
	link := filepath.Join(home(), ".local", "bin", "screenpets")
	if target, err := filepath.EvalSymlinks(link); err == nil && target == paths.Exe {
		_ = os.Remove(link)
	}
	_ = os.Remove(filepath.Join(home(), ".local", "share", "applications", "screenpets.desktop"))
	_ = os.RemoveAll(stateDir())
	if fileExists(filepath.Join(paths.Root, "bin", filepath.Base(paths.Exe))) && dirExists(filepath.Join(paths.Root, "personagens")) &&
		!dirExists(filepath.Join(paths.Root, "go")) {
		_ = os.RemoveAll(paths.Root)
	}
	if runtime.GOOS == "darwin" {
		_ = os.Remove(filepath.Dir(stateDir())) // ~/Library/Application Support/ScreenPets, se ficou vazia
	}
	fmt.Println("Pronto, o Screen Pets foi desinstalado. Tchau, bichinhos!")
}
