//go:build darwin

package main

import (
	"sync"
	"sync/atomic"

	"github.com/ebitengine/purego"
	"github.com/ebitengine/purego/objc"
	"github.com/hajimehoshi/ebiten/v2"
)

// macOS: o AppKit só aceita estas chamadas na thread principal, então elas vão para a fila principal
// (dispatch_async) e o resultado fica guardado para o Update ler.

type nsRect struct{ X, Y, W, H float64 }

// margens da área de trabalho (barra de menus em cima, Dock embaixo ou dos lados), em pontos
var macMargins atomic.Pointer[[4]int] // cima, baixo, esquerda, direita

var mainQueueCall func(fn func())

func init() {
	defer func() { recover() }()
	lib, err := purego.Dlopen("/usr/lib/libSystem.B.dylib", purego.RTLD_GLOBAL)
	if err != nil {
		return
	}
	mainQueue, err := purego.Dlsym(lib, "_dispatch_main_q")
	if err != nil {
		return
	}
	var dispatchAsyncF func(queue, ctx, fn uintptr)
	purego.RegisterLibFunc(&dispatchAsyncF, lib, "dispatch_async_f")
	var mu sync.Mutex
	var queue []func()
	cb := purego.NewCallback(func(ctx uintptr) uintptr {
		mu.Lock()
		list := queue
		queue = nil
		mu.Unlock()
		for _, fn := range list {
			func() {
				defer func() { recover() }()
				fn()
			}()
		}
		return 0
	})
	mainQueueCall = func(fn func()) {
		mu.Lock()
		queue = append(queue, fn)
		mu.Unlock()
		dispatchAsyncF(mainQueue, 0, cb)
	}
}

// Sem ícone no Dock e fora do Cmd+Tab (NSApplicationActivationPolicyAccessory), em todas as mesas (Spaces)
// e fora das que têm um app em tela cheia (sem FullScreenAuxiliary): assim um vídeo em tela cheia fica sem bichinhos.
func macAccessory() {
	if mainQueueCall == nil {
		return
	}
	mainQueueCall(func() {
		app := objc.ID(objc.GetClass("NSApplication")).Send(objc.RegisterName("sharedApplication"))
		app.Send(objc.RegisterName("setActivationPolicy:"), 1)
		windows := app.Send(objc.RegisterName("windows"))
		n := int(objc.Send[uint](windows, objc.RegisterName("count")))
		for i := 0; i < n; i++ {
			w := windows.Send(objc.RegisterName("objectAtIndex:"), uint(i))
			// CanJoinAllSpaces | Stationary | IgnoresCycle
			w.Send(objc.RegisterName("setCollectionBehavior:"), uint(1<<0|1<<4|1<<6))
		}
		readMargins()
	})
}

func readMargins() {
	screen := objc.ID(objc.GetClass("NSScreen")).Send(objc.RegisterName("mainScreen"))
	if screen == 0 {
		return
	}
	f := objc.Send[nsRect](screen, objc.RegisterName("frame"))
	v := objc.Send[nsRect](screen, objc.RegisterName("visibleFrame"))
	if v.W <= 0 || v.H <= 0 {
		return
	}
	// coordenadas do Cocoa: a origem fica embaixo
	m := [4]int{
		int(f.Y + f.H - (v.Y + v.H)), // cima: barra de menus
		int(v.Y - f.Y),               // baixo: Dock
		int(v.X - f.X),               // esquerda: Dock à esquerda
		int(f.X + f.W - (v.X + v.W)), // direita: Dock à direita
	}
	macMargins.Store(&m)
}

func workArea() rect {
	if mainQueueCall != nil {
		mainQueueCall(readMargins) // o Dock pode mudar de tamanho ou se esconder
	}
	w, h := ebiten.Monitor().Size()
	m := [4]int{25, 70, 0, 0} // até ler do sistema
	if p := macMargins.Load(); p != nil {
		m = *p
	}
	return rect{m[2], m[0], max(100, w-m[2]-m[3]), max(100, h-m[0]-m[1])}
}

func watchFullscreen()   {}
func fullscreenOn() bool { return false } // os Spaces de tela cheia já ficam sem os bichinhos
