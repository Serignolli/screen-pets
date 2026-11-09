package main

import (
	"os"
	"path/filepath"
	"runtime"
	"strings"
)

// "Abrir junto com o computador": no macOS um LaunchAgent, no Linux um atalho em ~/.config/autostart
// (o padrão XDG que GNOME, KDE, XFCE e companhia leem). Os dois só chamam "screenpets rodar".

func autostartFile() string {
	switch runtime.GOOS {
	case "darwin":
		return filepath.Join(home(), "Library", "LaunchAgents", "com.serignolli.screenpets.plist")
	case "linux":
		dir := os.Getenv("XDG_CONFIG_HOME")
		if dir == "" {
			dir = filepath.Join(home(), ".config")
		}
		return filepath.Join(dir, "autostart", "screenpets.desktop")
	}
	return ""
}

func autostartOn() bool { f := autostartFile(); return f != "" && fileExists(f) }

func setAutostart(on bool) error {
	f := autostartFile()
	if f == "" {
		return nil
	}
	if !on {
		if err := os.Remove(f); err != nil && !os.IsNotExist(err) {
			return err
		}
		return nil
	}
	var content string
	if runtime.GOOS == "darwin" {
		content = `<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>com.serignolli.screenpets</string>
  <key>ProgramArguments</key>
  <array><string>` + xmlEscape(paths.Exe) + `</string><string>rodar</string></array>
  <key>RunAtLoad</key><true/>
  <key>ProcessType</key><string>Interactive</string>
</dict>
</plist>
`
	} else {
		content = "[Desktop Entry]\nType=Application\nName=Screen Pets\nComment=Bichinhos em pixel art nas bordas da tela\n" +
			"Exec=\"" + paths.Exe + "\" rodar\nIcon=" + rootPath("recursos", "icone.svg") + "\nTerminal=false\nX-GNOME-Autostart-enabled=true\n"
	}
	if err := os.MkdirAll(filepath.Dir(f), 0o755); err != nil {
		return err
	}
	return os.WriteFile(f, []byte(content), 0o644)
}

func xmlEscape(s string) string {
	return strings.NewReplacer("&", "&amp;", "<", "&lt;", ">", "&gt;").Replace(s)
}
