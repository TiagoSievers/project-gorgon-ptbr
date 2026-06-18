#!/usr/bin/env bash
# Baixa BepInEx IL2CPP para vendor/ (incluído no tarball da Release)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VENDOR_DIR="${VENDOR_DIR:-$ROOT/vendor}"
ZIP="$VENDOR_DIR/BepInExPack_IL2CPP.zip"
BEPINEX_URL="${BEPINEX_URL:-https://thunderstore.io/package/download/BepInEx/BepInExPack_IL2CPP/6.0.755/}"

die() { echo "Erro: $*" >&2; exit 1; }

command -v curl >/dev/null || die "curl necessário"
mkdir -p "$VENDOR_DIR"

if [[ -f "$ZIP" ]] && file "$ZIP" | grep -q 'Zip archive' \
   && "$ROOT/scripts/validate-bepinex-zip.sh" "$ZIP" 2>/dev/null; then
  echo "Já existe (OK): $ZIP ($(du -sh "$ZIP" | cut -f1))"
  exit 0
fi

[[ -f "$ZIP" ]] && rm -f "$ZIP"
echo "Baixando BepInEx IL2CPP → $ZIP"
curl -fsSL --retry 3 --retry-delay 2 "$BEPINEX_URL" -o "$ZIP"
chmod +x "$ROOT/scripts/validate-bepinex-zip.sh" 2>/dev/null || true
file "$ZIP" | grep -q 'Zip archive' || die "Download inválido: $ZIP"
"$ROOT/scripts/validate-bepinex-zip.sh" "$ZIP" || die "Zip corrompido após download: $ZIP"
echo "OK: $ZIP ($(du -sh "$ZIP" | cut -f1))"
