//go:build !windows

package main

import (
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"
	"syscall"
)

// Abre "screenpets <args>" solto do terminal (senão os bichinhos somem quando o terminal fecha).
func launchDetached(args ...string) {
	cmd := exec.Command(paths.Exe, args...)
	cmd.Dir = filepath.Dir(paths.Exe)
	cmd.SysProcAttr = &syscall.SysProcAttr{Setsid: true}
	if err := cmd.Start(); err != nil {
		logf("abrir %v: %v", args, err)
		return
	}
	_ = cmd.Process.Release()
}

type instanceLock struct{ f *os.File }

// Uma cópia só: trava um arquivo em <estado>/<nome>.lock.
func acquireLock(name string) (*instanceLock, bool) {
	_ = os.MkdirAll(stateDir(), 0o755)
	f, err := os.OpenFile(filepath.Join(stateDir(), name+".lock"), os.O_CREATE|os.O_RDWR, 0o644)
	if err != nil {
		return nil, false
	}
	if err := syscall.Flock(int(f.Fd()), syscall.LOCK_EX|syscall.LOCK_NB); err != nil {
		f.Close()
		return nil, false
	}
	_ = f.Truncate(0)
	_, _ = f.WriteString(strconv.Itoa(os.Getpid()))
	return &instanceLock{f}, true
}

func (l *instanceLock) Release() {
	if l == nil {
		return
	}
	_ = syscall.Flock(int(l.f.Fd()), syscall.LOCK_UN)
	l.f.Close()
}

func isRunning(name string) bool {
	l, ok := acquireLock(name)
	if ok {
		l.Release()
		return false
	}
	return true
}

// Fecha os bichinhos que estão na tela, pelo pid gravado no arquivo de trava.
func killOthers() {
	if !isRunning("bichinhos") {
		return
	}
	data, err := os.ReadFile(filepath.Join(stateDir(), "bichinhos.lock"))
	if err != nil {
		return
	}
	if pid, err := strconv.Atoi(strings.TrimSpace(string(data))); err == nil && pid != os.Getpid() {
		_ = syscall.Kill(pid, syscall.SIGTERM)
	}
}
