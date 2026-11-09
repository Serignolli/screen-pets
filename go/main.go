// Screen Pets para macOS e Linux: o mesmo Screen Pets do Windows (src/, em C#), escrito em Go com o Ebitengine.
// Lê os mesmos personagens/ e o mesmo formato de config.json.
//
//	screenpets               configurar (no terminal) e pôr os bichinhos na tela
//	screenpets mostrar       pôr os bichinhos na tela
//	screenpets esconder      tirar os bichinhos da tela (até mostrar de novo)
//	screenpets desinstalar   apagar o programa
//	screenpets rodar         os bichinhos em si (é o que o "abrir junto com o computador" chama)
//	screenpets foto [s] [arquivo.png] [config.json]   simula alguns segundos e desenha numa tela de mentira
package main

import (
	"fmt"
	"os"
	"runtime/debug"
	"strconv"
	"strings"
)

const version = "1.1.0"

func main() {
	cmd := ""
	if len(os.Args) > 1 {
		cmd = strings.ToLower(os.Args[1])
	}
	defer func() {
		if r := recover(); r != nil {
			logf("%s: %v\n%s", cmd, r, debug.Stack())
			fmt.Fprintln(os.Stderr, "Algo deu errado:", r, "(detalhes em "+logFile()+")")
			os.Exit(1)
		}
	}()
	arg := func(i int, def string) string {
		if len(os.Args) > i {
			return os.Args[i]
		}
		return def
	}
	switch cmd {
	case "", "config":
		runConfig()
	case "rodar", "run", "--fundo":
		runPets()
	case "mostrar":
		cfg := loadConfig()
		cfg.Enabled = true
		apply(cfg)
		fmt.Println("Os bichinhos estão na tela.")
	case "esconder":
		cfg := loadConfig()
		cfg.Enabled = false
		apply(cfg)
		fmt.Println("Bichinhos escondidos. Para eles voltarem: screenpets mostrar")
	case "sair", "quit":
		killOthers()
	case "religar":
		// usado pelo install.sh depois de atualizar: volta os bichinhos se eles estavam ligados
		if cfg := loadConfig(); cfg.Enabled && len(cfg.Groups) > 0 && !isRunning("bichinhos") {
			launchDetached("rodar")
		}
	case "abrir-junto":
		// usado pelo install.sh: liga o "abrir junto com o computador" na primeira instalação
		if err := setAutostart(arg(2, "sim") != "nao"); err != nil {
			fmt.Fprintln(os.Stderr, "abrir junto com o computador:", err)
		}
	case "desinstalar", "uninstall":
		uninstallEverything()
	case "foto":
		secs, err := strconv.ParseFloat(arg(2, "6"), 64)
		if err != nil {
			secs = 6
		}
		runPhoto(secs, arg(3, "foto.png"), arg(4, configFile()))
	case "versao", "version", "--version", "-v":
		fmt.Println("Screen Pets " + version)
	case "ajuda", "help", "--help", "-h":
		fmt.Println("Screen Pets " + version + ": bichinhos em pixel art que passeiam pelas bordas da sua tela.")
		fmt.Println()
		fmt.Println("  screenpets               configurar (quem aparece, onde, tamanho...)")
		fmt.Println("  screenpets mostrar       pôr os bichinhos na tela")
		fmt.Println("  screenpets esconder      tirar os bichinhos da tela")
		fmt.Println("  screenpets desinstalar   apagar o programa")
	default:
		fmt.Fprintln(os.Stderr, "comando desconhecido: "+cmd+" (veja: screenpets ajuda)")
		os.Exit(2)
	}
}
