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

[[ -f "$PACK_DIR/INSTALAR.exe" ]] || die "INSTALAR.exe ausente em $PACK_DIR"
[[ -f "$PACK_DIR/dist/Translator.dll" ]] || die "dist/Translator.dll ausente no pacote"
[[ -d "$PACK_DIR/output/pt-BR" ]] || die "output/pt-BR/ ausente no pacote"
[[ ! -f "$PACK_DIR/dist/INSTALAR.exe" ]] || die "dist/INSTALAR.exe não deve estar no pacote (duplicata)"
[[ ! -f "$PACK_DIR/dist/INSTALAR.pdb" ]] || die "dist/INSTALAR.pdb não deve estar no pacote (debug)"

[[ -f "$VERSION_FILE" ]] || VERSION_FILE="$PACK_DIR/output/Translation/version.json"
[[ -f "$VERSION_FILE" ]] || die "version.json ausente (output/Translation/)"

PACK_VERSION="$(python3 -c "import json; print(json.load(open(r'''$VERSION_FILE'''))['Version'])")"
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
echo "Jogador: extrair → pasta pg-ptbr-windows → INSTALAR.exe; desinstalar: uninstall-language-pack-ptbr.exe"
