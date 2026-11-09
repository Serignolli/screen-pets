package main

import (
	"fmt"
	"os"
	"path/filepath"
	"runtime"
	"time"
)

// Os mesmos lugares do programa do Windows (src/Data.cs, classe Paths). A instalação fica assim:
//
//	<raiz>/bin/screenpets     este programa
//	<raiz>/personagens/       os bichinhos
//	<raiz>/recursos/          a fonte (na pasta do projeto: src/recursos)
//	<raiz>/config.json        a configuração
var paths struct {
	Exe, Root string
}

func init() {
	exe, err := os.Executable()
	if err == nil {
		if real, err := filepath.EvalSymlinks(exe); err == nil {
			exe = real // ~/.local/bin/screenpets é um atalho para a instalação
		}
	}
	paths.Exe = exe
	paths.Root = filepath.Clean(filepath.Join(filepath.Dir(exe), ".."))
}

func rootPath(parts ...string) string {
	return filepath.Join(append([]string{paths.Root}, parts...)...)
}

func configFile() string    { return rootPath("config.json") }
func charactersDir() string { return rootPath("personagens") }

func resourcesDir() string {
	if d := rootPath("recursos"); dirExists(d) {
		return d
	}
	return rootPath("src", "recursos")
}

func home() string {
	h, err := os.UserHomeDir()
	if err != nil {
		return "."
	}
	return h
}

// Trava de instância única e o log.
func stateDir() string {
	switch runtime.GOOS {
	case "darwin":
		return filepath.Join(home(), "Library", "Application Support", "ScreenPets", "estado")
	case "windows":
		// só para testar o port no Windows, sem misturar com o programa de verdade (em C#)
		return filepath.Join(os.Getenv("LOCALAPPDATA"), "ScreenPets-go")
	default:
		if x := os.Getenv("XDG_STATE_HOME"); x != "" {
			return filepath.Join(x, "screenpets")
		}
		return filepath.Join(home(), ".local", "state", "screenpets")
	}
}

func logFile() string { return filepath.Join(stateDir(), "log.txt") }

func logf(format string, args ...any) {
	_ = os.MkdirAll(stateDir(), 0o755)
	if fi, err := os.Stat(logFile()); err == nil && fi.Size() > 200000 {
		_ = os.Remove(logFile())
	}
	f, err := os.OpenFile(logFile(), os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0o644)
	if err != nil {
		return
	}
	defer f.Close()
	fmt.Fprintf(f, "%s  %s\n", time.Now().Format("2006-01-02 15:04:05"), fmt.Sprintf(format, args...))
}

func dirExists(p string) bool  { fi, err := os.Stat(p); return err == nil && fi.IsDir() }
func fileExists(p string) bool { fi, err := os.Stat(p); return err == nil && !fi.IsDir() }

func stamp(f string) time.Time {
	if fi, err := os.Stat(f); err == nil {
		return fi.ModTime()
	}
	return time.Time{}
}
