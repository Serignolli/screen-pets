//go:build linux

package main

import (
	"os/exec"
	"regexp"
	"strconv"
	"strings"
	"sync/atomic"
	"time"

	"github.com/hajimehoshi/ebiten/v2"
)

// Linux: a área de trabalho (sem os painéis) e a tela cheia vêm do gerenciador de janelas, pelo xprop.
// Sem xprop (ou sem X11/XWayland), os bichinhos usam a tela toda e não somem em tela cheia.

func macAccessory() {}

var numbers = regexp.MustCompile(`\d+`)

func xprop(args ...string) string {
	out, err := exec.Command("xprop", args...).Output()
	if err != nil {
		return ""
	}
	return string(out)
}

// _NET_WORKAREA(CARDINAL) = x, y, largura, altura (em pixels; a primeira mesa basta)
func readWorkArea() *[4]float64 {
	out := xprop("-root", "_NET_WORKAREA")
	i := strings.Index(out, "=")
	if i < 0 {
		return nil
	}
	nums := numbers.FindAllString(out[i:], 4)
	if len(nums) < 4 {
		return nil
	}
	var v [4]float64
	for k, s := range nums {
		v[k], _ = strconv.ParseFloat(s, 64)
	}
	return &v
}

var area atomic.Pointer[[4]float64]

func workArea() rect {
	w, h := ebiten.Monitor().Size()
	full := rect{0, 0, w, h}
	p := area.Load()
	if p == nil {
		if p = readWorkArea(); p == nil {
			return full
		}
		area.Store(p)
	}
	v := *p
	dev := ebiten.Monitor().DeviceScaleFactor()
	if dev <= 0 {
		dev = 1
	}
	r := rect{int(v[0] / dev), int(v[1] / dev), int(v[2] / dev), int(v[3] / dev)}
	// com vários monitores a área pode passar do principal: fica só com o pedaço dele
	r.W = min(r.W, w-r.X)
	r.H = min(r.H, h-r.Y)
	if r.W < 100 || r.H < 100 {
		return full
	}
	return r
}

var fullscreen atomic.Bool

// Confere a janela ativa e os painéis a cada 1,5 s (fora do ciclo de desenho, porque chama o xprop).
func watchFullscreen() {
	if _, err := exec.LookPath("xprop"); err != nil {
		return
	}
	go func() {
		for {
			fullscreen.Store(activeIsFullscreen())
			if p := readWorkArea(); p != nil {
				area.Store(p)
			}
			time.Sleep(1500 * time.Millisecond)
		}
	}()
}

func activeIsFullscreen() bool {
	out := xprop("-root", "_NET_ACTIVE_WINDOW")
	i := strings.Index(out, "#")
	if i < 0 {
		return false
	}
	id := strings.Fields(out[i+1:])
	if len(id) == 0 || id[0] == "0x0" {
		return false
	}
	return strings.Contains(xprop("-id", strings.TrimSuffix(id[0], ","), "_NET_WM_STATE"), "_NET_WM_STATE_FULLSCREEN")
}

func fullscreenOn() bool { return fullscreen.Load() }
