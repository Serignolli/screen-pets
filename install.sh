#!/bin/sh
# Screen Pets para macOS e Linux: baixa a última versão, instala e põe os bichinhos na tela.
#
#   curl -fsSL https://screenpets.serignolli.com/install.sh | sh
#
# Instala em ~/.local/share/screenpets (Linux) ou ~/Library/Application Support/ScreenPets/app (macOS),
# com o atalho ~/.local/bin/screenpets. Não precisa de sudo. Rodar de novo atualiza (a configuração fica).
set -eu

# de onde baixar os pacotes (o site, que também serve este arquivo)
BASE="${SCREENPETS_URL:-https://screenpets.serignolli.com}"

case "$(uname -s)" in
  Darwin) os=darwin ;;
  Linux) os=linux ;;
  *) echo "Este instalador é para macOS e Linux. No Windows, baixe o ScreenPets-Setup.exe em $BASE"
     exit 1 ;;
esac
case "$(uname -m)" in
  x86_64|amd64) arch=amd64 ;;
  arm64|aarch64) arch=arm64 ;;
  *) echo "Processador não suportado: $(uname -m)"; exit 1 ;;
esac

if [ "$os" = darwin ]; then
  dest="$HOME/Library/Application Support/ScreenPets/app"
else
  dest="${XDG_DATA_HOME:-$HOME/.local/share}/screenpets"
fi
url="$BASE/screenpets-$os-$arch.tar.gz"
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

echo "Baixando o Screen Pets ($os/$arch)..."
if command -v curl >/dev/null 2>&1; then
  curl -fsSL "$url" -o "$tmp/screenpets.tar.gz"
else
  wget -q "$url" -O "$tmp/screenpets.tar.gz"
fi
tar -xzf "$tmp/screenpets.tar.gz" -C "$tmp"

updating=false
[ -f "$dest/config.json" ] && updating=true
[ -x "$dest/bin/screenpets" ] && "$dest/bin/screenpets" sair >/dev/null 2>&1 || true

# atualização: troca o programa e os bichinhos; o config.json fica
mkdir -p "$dest"
rm -rf "$dest/bin" "$dest/personagens" "$dest/recursos"
cp -R "$tmp/screenpets/." "$dest/"
chmod +x "$dest/bin/screenpets"
[ "$os" = darwin ] && xattr -dr com.apple.quarantine "$dest" 2>/dev/null || true

mkdir -p "$HOME/.local/bin"
ln -sf "$dest/bin/screenpets" "$HOME/.local/bin/screenpets"

# Linux: "Screen Pets" no menu de aplicativos abre a configuração num terminal
if [ "$os" = linux ]; then
  apps="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
  mkdir -p "$apps"
  cat > "$apps/screenpets.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Screen Pets
Comment=Bichinhos em pixel art nas bordas da tela
Exec="$dest/bin/screenpets"
Icon=$dest/recursos/icone.svg
Terminal=true
Categories=Utility;
EOF
fi
echo "Instalado em $dest"

case ":$PATH:" in
  *":$HOME/.local/bin:"*) ;;
  *) echo
     echo "Dica: ~/.local/bin não está no seu PATH. Para usar o comando screenpets, adicione ao seu shell:"
     echo "  export PATH=\"\$HOME/.local/bin:\$PATH\"" ;;
esac

if [ "$updating" = true ]; then
  "$dest/bin/screenpets" religar
  echo "Screen Pets atualizado! A sua turma continua a mesma. Para mudar: screenpets"
  exit 0
fi

# primeira vez: liga junto com o computador e chama os bichinhos
"$dest/bin/screenpets" abrir-junto sim
"$dest/bin/screenpets" mostrar
echo
echo "Os bichinhos chegaram! Eles voltam sozinhos sempre que o computador ligar."
if [ -r /dev/tty ]; then
  echo "Agora escolha quem aparece, onde e o tamanho (dá para mudar depois com: screenpets)"
  "$dest/bin/screenpets" < /dev/tty
else
  echo "Para escolher quem aparece, onde e o tamanho: screenpets"
fi
