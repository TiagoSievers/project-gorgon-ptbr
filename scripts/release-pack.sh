#!/usr/bin/env bash
# Compacta pack/pg-ptbr/ → releases/*.tar.gz e *.zip (WinRAR / 7-Zip)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PACK_DIR="${PACK_DIR:-$ROOT/pack/pg-ptbr}"
RELEASES_DIR="${RELEASES_DIR:-$ROOT/releases}"
VERSION_FILE="$ROOT/output/Translation/version.json"

die() { echo "Erro: $*" >&2; exit 1; }

[[ -d "$PACK_DIR/scripts" && -f "$PACK_DIR/INSTALAR" ]] || {
  chmod +x "$ROOT/scripts/assemble-pack.sh"
  "$ROOT/scripts/assemble-pack.sh"
}

[[ -f "$VERSION_FILE" ]] || die "version.json ausente"

PACK_VERSION="$(python3 -c "import json; print(json.load(open('$VERSION_FILE'))['Version'])")"
CDN_VERSION="$(python3 -c "import json; print(json.load(open('$VERSION_FILE'))['CdnFileVersion'])")"
STAMP="$(date -u +%Y%m%d)"
BASE="pg-ptbr-${PACK_VERSION}-cdn${CDN_VERSION}-${STAMP}-linux"
TAR_OUT="$RELEASES_DIR/${BASE}.tar.gz"
ZIP_OUT="$RELEASES_DIR/${BASE}.zip"

mkdir -p "$RELEASES_DIR"
PACK_PARENT="$(dirname "$PACK_DIR")"
PACK_NAME="$(basename "$PACK_DIR")"

tar -czf "$TAR_OUT" -C "$PACK_PARENT" "$PACK_NAME"

command -v zip >/dev/null 2>&1 || die "zip não encontrado (sudo apt install zip)"
rm -f "$ZIP_OUT"
(cd "$PACK_PARENT" && zip -qr "$ZIP_OUT" "$PACK_NAME")

echo ""
echo "Envie para seu amigo (WinRAR / 7-Zip):"
echo "  ZIP: $ZIP_OUT"
echo "  TAR: $TAR_OUT"
du -sh "$ZIP_OUT" "$TAR_OUT"
echo ""
echo "Amigo: extrair → pasta pg-ptbr → dois cliques em INSTALAR → ler COMO-INSTALAR.txt"
