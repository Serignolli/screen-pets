# Screen Pets

**Bichinhos em pixel art que passeiam pelas bordas da sua tela.** A versão do [CmdCrew](../cmd-crew) para quem não usa terminal: sem agente, sem configuração técnica, só companhia.

- **Terrestres** andam em cima da barra de tarefas: gatos, cachorros, pinguins, formigas e coelhos.
- **Escaladores** sobem e descem pelas laterais da tela: lagartixa, joaninha, caracol, aranha e besouro.
- **Voadores** voam no alto da tela: pássaros, borboletas, abelhas, morcegos, dragão.

Cada tipo tem o seu canto: os terrestres ficam só na barra de tarefas; escaladores e voadores dividem o alto e as laterais (uma aranha pode passear pelo teto, uma borboleta pode voar perto da lateral).

Eles não atrapalham: o mouse passa direto por eles, não aparecem no Alt+Tab, nunca roubam o foco e somem sozinhos quando um vídeo, jogo ou apresentação ocupa a tela inteira.

Windows, macOS e Linux · grátis · 100% local, sem conta e sem internet.

## Instalar

1. Abra o **ScreenPets-Setup.exe** e clique em **Instalar**. Não precisa de administrador.
2. Pronto: os bichinhos aparecem e voltam sozinhos sempre que o computador ligar.

Ele se instala em `%LOCALAPPDATA%\Programs\ScreenPets`, cria o atalho **Screen Pets** na área de trabalho e no menu Iniciar, liga junto com o Windows e aparece em **Aplicativos instalados**, que é por onde se desinstala (também dá para desinstalar pela própria tela de configuração).

> O Windows pode mostrar "o Windows protegeu o computador" porque o executável não é assinado. É só clicar em "Mais informações" → "Executar assim mesmo".

### macOS e Linux

Cole no terminal:

```sh
curl -fsSL https://screenpets.serignolli.com/install.sh | sh
```

Instala sem sudo em `~/Library/Application Support/ScreenPets/app` (macOS) ou `~/.local/share/screenpets` (Linux), com o comando `~/.local/bin/screenpets`, liga junto com o computador e abre a configuração no terminal. Rodar de novo atualiza (a turma configurada fica). Se o site estiver em outro endereço: `curl -fsSL <endereço>/install.sh | SCREENPETS_URL=<endereço> sh`.

| Comando | O que faz |
|---|---|
| `screenpets` | configurar (quem aparece, onde, tamanho, velocidade, abrir junto com o computador) |
| `screenpets mostrar` / `esconder` | pôr ou tirar os bichinhos da tela |
| `screenpets desinstalar` | apagar o programa |

No Linux precisa de X11 ou XWayland e de um compositor (para a transparência); a área sem os painéis e a "tela cheia" vêm do `xprop`. No macOS eles ficam em todas as mesas (Spaces) menos nas de apps em tela cheia.

## Configurar

Clique no ícone dos bichinhos perto do relógio (ou abra **Screen Pets** pelo atalho). A tela tem três partes:

- **Na sua tela** (esquerda): as turmas de bichinhos. **Adicionar bichinho** abre a escolha, separada em terrestres, escaladores e voadores.
- **A turma escolhida** (direita): trocar o bichinho, a cor (ou misturar as cores), quantos, onde ficam (barra de tarefas, alto da tela, as duas laterais ou só uma) e o tamanho.
- **Embaixo**: mostrar/esconder todos, balõezinhos de fala, sumir em tela cheia, abrir com o Windows e velocidade.

Tudo vale na hora, não tem botão de salvar. Clicando com o botão direito no ícone perto do relógio dá para **esconder** os bichinhos rapidinho (numa reunião, por exemplo) ou **sair**.

## Personagens

Cada personagem é um arquivo JSON em [`personagens/`](personagens), no mesmo formato do CmdCrew: o desenho em texto (cada letra é uma cor da skin, `.` é transparente). Os terrestres vieram do CmdCrew. Campos novos:

| Campo | O que faz |
|---|---|
| `tipo` | `terrestre`, `escalador` ou `voador`: em que grupo aparece na escolha e onde fica por padrão |
| `ritmo` | velocidade própria (o caracol é `0.35`, o passarinho é `1.4`) |
| `falas` | frases dos balõezinhos |

Os escaladores são desenhados vistos de cima (como se vê uma lagartixa na parede) e o programa gira o desenho para cada lateral. Os voadores usam `voo` (altura acima do chão) e nunca giram.

## Para quem desenvolve

Não precisa instalar nada: o executável é compilado com o compilador C# que já vem no Windows (.NET Framework 4).

| Comando | O que faz |
|---|---|
| `powershell -File src\build.ps1` | compila `bin\ScreenPets.exe` e `dist\ScreenPets-Setup.exe` |
| `scripts\abrir.cmd` | abre usando esta pasta (lê `personagens\` e grava `config.json` aqui) |
| `bin\ScreenPets.exe folha saida.png` | folha com todos os desenhos, para conferir a arte |
| `bin\ScreenPets.exe foto 6 saida.png [config.json]` | simula 6 segundos e desenha as bordas numa tela de mentira (sem abrir janela) |
| `bin\ScreenPets.exe telas pasta` | desenha a configuração, a escolha e o instalador em PNG |
| `bin\ScreenPets.exe setup [pronto\|erro]` | abre o instalador (as páginas extras só mostram o visual) |
| `python ferramentas\gerar_icone.py` | refaz o ícone a partir do gato |
| `python ferramentas\gerar_site.py` | refaz `site\dados.js` a partir de `personagens\` (rode depois de mexer num bichinho) |
| `sh go/empacotar.sh` | compila o port de macOS/Linux (Go) para os 4 alvos em `dist/` e copia os `.tar.gz` e o `install.sh` para `site/` |
| `go\screenpets.exe foto 6 saida.png [config.json]` | o "foto" do port (compile com `cd go && go build -o screenpets.exe .`) |
| `cd site && python -m http.server 8765` | abre o site em http://localhost:8765 (também funciona abrindo `site\index.html` direto) |

Rodando de `bin\` com `personagens\` ao lado, o programa se comporta como instalado; o `ScreenPets-Setup.exe` sozinho (sem a pasta) é o instalador.

### Estrutura

```
src/
  Program.cs     comandos e instância única (abrir de novo só traz a configuração para a frente)
  Host.cs        o programa rodando: ícone perto do relógio, palcos e "sumir em tela cheia"
  Stage.cs       uma faixa transparente por borda; a lógica é a do Overlay do CmdCrew num espaço
                 "deitado" que uma matriz leva para o chão, o teto ou as laterais
  ConfigForm.cs  a tela de configuração e a escolha de bichinhos
  SetupForm.cs   o instalador (Installer.cs copia, cria atalhos e registra)
  Data.cs        personagens, configuração (turmas) e desenho em pixel art
  Theme.cs, CrewBanner.cs, Json.cs   iguais aos do CmdCrew
go/              macOS e Linux (Go + Ebitengine, sem cgo): stage.go é o Stage.cs; uma janela transparente
                 cobre a área de trabalho e cada borda é um Stage dentro dela; tui.go é a configuração no terminal
personagens/     os bichinhos (JSON), embutidos no instalador
ferramentas/     scripts Python
site/            o site (estático): simulador das bordas em JS (sprites.js espelha o Stage.cs),
                 galeria e download (o build copia o ScreenPets-Setup.exe para cá; o empacotar.sh, os pacotes de macOS/Linux e o install.sh)
```

A configuração fica em `config.json` ao lado de `bin\`:

```json
{
  "ativo": true,
  "baloes": true,
  "esconderTelaCheia": true,
  "velocidade": 1,
  "turmas": [
    { "personagem": "gatos", "cor": "*", "lugar": "chao", "quantidade": 2, "tamanho": 2 },
    { "personagem": "lagartixa", "cor": "verde", "lugar": "laterais", "quantidade": 2, "tamanho": 2 }
  ]
}
```

`lugar`: `chao`, `topo`, `laterais`, `esquerda` ou `direita` (terrestres só `chao`; escaladores e voadores qualquer um menos `chao`, senão o programa usa o lugar natural deles). `tamanho`: de 1 a 5.

Por enquanto os bichinhos ficam só no monitor principal.
