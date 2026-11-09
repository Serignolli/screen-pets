# Screen Pets — alterações (2026-10-15)

- [x] Renomear Screen Crew → Screen Pets (programa, instalador, site, README, scripts)
- [x] Instalador limpa a instalação antiga "ScreenCrew" (atalhos, iniciar com o Windows, registro) e leva a config
- [x] 5 bichinhos por categoria: +Coelhos (terrestre), +Besouro (escalador); voadores já tinham 5
- [x] Site: seção Supporters Pack (HD, animações melhores) como "em breve"
- [x] macOS e Linux: port em Go/Ebitengine (go/), install.sh, pacotes .tar.gz (go/empacotar.sh)
- [x] Site: download para macOS/Linux (comando para colar no terminal + copiar)
- [x] Verificação: build.ps1, go vet/gofmt/build nos 4 alvos, folha, foto C# × Go, janela ao vivo (Windows, opaca), configuração no terminal, site no navegador
- [x] README atualizado
- [ ] Testar num Mac e num Linux de verdade (sem máquina/WSL/Docker aqui)
- [ ] Confirmar o domínio do site (install.sh assume https://screenpets.serignolli.com)

# Prévia do Supporters Pack + apoie (2026-11-09)

- [x] 15 desenhos HD no PixelLab (1 por bichinho, 64×64, vista de lado): `supporters-pack/arte/pixellab/` e `site/img/hd/`
- [x] Site: comparação "Hoje → No pack" com o desenho HD de verdade + grade da prévia (clique troca a comparação)
- [x] Site: botão "Quero apoiar" → https://apoie.serignolli.com/suporte?pack=screenpets-hd
- [x] Site: "Pague um café pra turma" no rodapé e aviso de apoio no primeiro download (igual ao CmdCrew)
- [x] apoie (donation): galeria do pack screenpets-hd com 6 desenhos em `public/packs/screenpets/`
- [x] Verificação: node --check, npm run build do donation, navegador desktop e 390px
- [ ] Publicar o site do Screen Pets e o apoie (deploy não feito)
- [ ] Animações HD (andar etc.): cota do PixelLab do dia zerada

# Refazer a prévia HD (pedido de 2026-11-09)

- [x] Prévia: "Morceguinhos" → "Morcegos", "Passarinhos" → "Pássaros" (só no site; o programa continua igual)
- [x] Grade "Prévia: a turma inteira em HD" removida do site (pedido); fica só a comparação Hoje → No pack com o gato
- [ ] BLOQUEADO (cota do PixelLab zerada; volta +5 por noite): redesenhar menos "kawaii", fofo mas com proporções normais e poses variadas
  - Prioridade (5 da próxima cota): aranha (8 patas), caracol (casco), morcego (normal, só um pouco fofo), gato, cachorro
  - Depois: os outros 10, conforme a cota
- [x] Nomes sem diminutivo em todo lugar: "Morceguinhos" → "Morcegos", "Passarinhos" → "Pássaros" (personagens/*.json, site, README); exe e pacotes macOS/Linux recompilados
- [x] "dragãozinho" → "dragão" (personagens/dragao.json, site); exe e pacotes recompilados
- [x] README refeito no modelo do CmdCrew (instalar, configurar, personagens, Supporters Pack, privacidade, desenvolvimento, créditos)
- [x] LICENSE (MIT, igual ao CmdCrew), citado no README
- [x] site/img/*.png refeitas com `ScreenPets.exe telas` (as antigas ainda diziam "Screen Crew")
- [ ] Repositório público: o README não tem link de GitHub (não existe repo do Screen Pets ainda)
