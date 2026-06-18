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

[[ -f "$PACK_DIR/INSTALAR.exe" ]] || die \
  "INSTALAR.exe ausente em $PACK_DIR — gere no Windows: powershell -File scripts/build-windows-installer.ps1
   ou dispare GitHub Actions: .github/workflows/release.yml (workflow_dispatch)"

[[ -f "$VERSION_FILE" ]] || die "version.json ausente"

PACK_VERSION="$(python3 -c "import json; print(json.load(open('$VERSION_FILE'))['Version'])")"
ZIP_NAME="Project-Gorgon-PT-BR-v${PACK_VERSION}-Windows.zip"
ZIP_OUT="$RELEASES_DIR/${ZIP_NAME}"

mkdir -p "$RELEASES_DIR"
PACK_PARENT="$(dirname "$PACK_DIR")"
PACK_NAME="$(basename "$PACK_DIR")"

command -v zip >/dev/null 2>&1 || die "zip não encontrado"
rm -f "$ZIP_OUT"
(cd "$PACK_PARENT" && zip -qr "$ZIP_OUT" "$PACK_NAME")

echo ""
echo "Release Windows (GitHub → Releases → anexar este arquivo):"
echo "  $ZIP_OUT"
du -sh "$ZIP_OUT"
echo ""
echo "Jogador: extrair → pasta pg-ptbr-windows → dois cliques em INSTALAR.exe"
