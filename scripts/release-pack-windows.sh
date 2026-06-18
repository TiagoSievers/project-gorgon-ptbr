#!/usr/bin/env bash
# Compacta pack/pg-ptbr-windows/ → releases/*-windows.zip
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PACK_DIR="${PACK_DIR:-$ROOT/pack/pg-ptbr-windows}"
RELEASES_DIR="${RELEASES_DIR:-$ROOT/releases}"
VERSION_FILE="$ROOT/output/Translation/version.json"

die() { echo "Erro: $*" >&2; exit 1; }

[[ -d "$PACK_DIR/dist" ]] || {
  chmod +x "$ROOT/scripts/assemble-pack-windows.sh"
  "$ROOT/scripts/assemble-pack-windows.sh"
}

[[ -f "$VERSION_FILE" ]] || die "version.json ausente"

PACK_VERSION="$(python3 -c "import json; print(json.load(open('$VERSION_FILE'))['Version'])")"
CDN_VERSION="$(python3 -c "import json; print(json.load(open('$VERSION_FILE'))['CdnFileVersion'])")"
STAMP="$(date -u +%Y%m%d)"
BASE="pg-ptbr-${PACK_VERSION}-cdn${CDN_VERSION}-${STAMP}-windows"
ZIP_OUT="$RELEASES_DIR/${BASE}.zip"

mkdir -p "$RELEASES_DIR"
PACK_PARENT="$(dirname "$PACK_DIR")"
PACK_NAME="$(basename "$PACK_DIR")"

command -v zip >/dev/null 2>&1 || die "zip não encontrado"
rm -f "$ZIP_OUT"
(cd "$PACK_PARENT" && zip -qr "$ZIP_OUT" "$PACK_NAME")

echo ""
echo "Release Windows:"
echo "  ZIP: $ZIP_OUT"
du -sh "$ZIP_OUT"
echo ""
echo "Jogador: extrair → pg-ptbr-windows → dois cliques em INSTALAR.exe"
