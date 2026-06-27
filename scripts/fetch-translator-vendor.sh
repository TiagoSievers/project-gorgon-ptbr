#!/usr/bin/env bash
# Baixa mod Translator (PickTeam) → vendor/Translator/Translator.dll
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VENDOR_DIR="${VENDOR_DIR:-$ROOT/vendor/Translator}"
DLL="$VENDOR_DIR/Translator.dll"
TRANSLATOR_URL="${TRANSLATOR_URL:-https://thunderstore.io/package/download/PickTeam/Translator/0.2.0/}"

die() { echo "Erro: $*" >&2; exit 1; }

command -v curl >/dev/null || die "curl necessário"
mkdir -p "$VENDOR_DIR"

if [[ -f "$DLL" ]]; then
  echo "Já existe (OK): $DLL ($(du -sh "$DLL" | cut -f1))"
  exit 0
fi

staging="$(mktemp -d)"
zip="$staging/Translator.zip"
echo "Baixando Translator → $DLL"
curl -fsSL --retry 3 --retry-delay 2 "$TRANSLATOR_URL" -o "$zip"
file "$zip" | grep -q 'Zip archive' || die "Download inválido: $zip"

unzip -q "$zip" -d "$staging/extract"
src="$staging/extract/plugins/Translator/Translator.dll"
[[ -f "$src" ]] || die "Translator.dll não encontrado no zip Thunderstore"
cp "$src" "$DLL"
rm -rf "$staging"
echo "OK: $DLL ($(du -sh "$DLL" | cut -f1))"
