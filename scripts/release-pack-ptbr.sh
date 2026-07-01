#!/usr/bin/env bash
# Compacta pack/ptbr/ → releases/Project-Gorgon-PT-BR-v*-Pack.zip
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PACK_DIR="${PACK_DIR:-$ROOT/pack/ptbr}"
RELEASES_DIR="${RELEASES_DIR:-$ROOT/releases}"
VERSION_FILE="$ROOT/output/Translation/version.json"

die() { echo "Erro: $*" >&2; exit 1; }

[[ -d "$PACK_DIR/para-pasta-do-jogo" && -f "$PACK_DIR/LEIA-ME.txt" ]] || {
  chmod +x "$ROOT/scripts/assemble-pack-ptbr.sh"
  "$ROOT/scripts/assemble-pack-ptbr.sh"
}

[[ -f "$VERSION_FILE" ]] || die "version.json ausente"

PACK_VERSION="$(python3 -c "import json; print(json.load(open('$VERSION_FILE'))['Version'])")"
ZIP_NAME="Project-Gorgon-PT-BR-v${PACK_VERSION}-Pack.zip"
ZIP_OUT="$RELEASES_DIR/${ZIP_NAME}"

mkdir -p "$RELEASES_DIR"
PACK_PARENT="$(dirname "$PACK_DIR")"
PACK_NAME="$(basename "$PACK_DIR")"

command -v zip >/dev/null 2>&1 || die "zip não encontrado"
rm -f "$ZIP_OUT"
(cd "$PACK_PARENT" && zip -qr "$ZIP_OUT" "$PACK_NAME")

echo ""
echo "Release pack PT-BR:"
echo "  $ZIP_OUT"
du -sh "$ZIP_OUT"
