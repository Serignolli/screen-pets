#!/bin/sh
# Monta dist/screenpets-<sistema>-<arquitetura>.tar.gz para macOS e Linux e copia para site/ (junto com o
# install.sh), que é de onde o instalador baixa. Rodar na raiz do projeto ou em go/.
# O Ebitengine não precisa de cgo nesses sistemas, então tudo sai de uma máquina só (inclusive do Windows, com Git Bash).
#
#   sh go/empacotar.sh                 todos
#   sh go/empacotar.sh linux/amd64     só um
set -eu
cd "$(dirname "$0")/.."
targets=${*:-"darwin/arm64 darwin/amd64 linux/amd64 linux/arm64"}
mkdir -p dist
for t in $targets; do
  os=${t%/*}; arch=${t#*/}
  work=$(mktemp -d)
  pkg="$work/screenpets"
  mkdir -p "$pkg/bin" "$pkg/recursos"
  (cd go && GOOS=$os GOARCH=$arch CGO_ENABLED=0 go build -trimpath -ldflags="-s -w" -o "$pkg/bin/screenpets" .)
  cp -R personagens "$pkg/"
  cp src/recursos/PixelifySans.ttf src/recursos/OFL-PixelifySans.txt "$pkg/recursos/"
  cp site/favicon.svg "$pkg/recursos/icone.svg"
  tar -C "$work" -czf "dist/screenpets-$os-$arch.tar.gz" screenpets
  rm -rf "$work"
  cp "dist/screenpets-$os-$arch.tar.gz" site/
  echo "dist/screenpets-$os-$arch.tar.gz"
done
cp install.sh site/install.sh
