#!/usr/bin/env bash
# CLI da skill pg-ptbr-update-pack — monta pack/ptbr/ e gera releases/*.zip
# Uso: update-pack.sh [--full|--repack-only] [--dry-run]
set -euo pipefail

MODE="full"
DRY_RUN=0

die() { echo "ERRO: $*" >&2; exit 1; }

usage() {
  cat <<EOF
Uso: update-pack.sh [--full|--repack-only] [--dry-run]

  --full         make write + build-plugin + sync-dist + pack-ptbr + release + verify (padrão)
  --repack-only  só pack-ptbr + release-pack-ptbr + verify-pack-ptbr (output/ e dist/ já prontos)
  --dry-run      mostra plano sem executar
EOF
  exit 1
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --full) MODE=full; shift ;;
    --repack-only) MODE=repack-only; shift ;;
    --dry-run) DRY_RUN=1; shift ;;
    -h|--help) usage ;;
    *) die "argumento inesperado: $1" ;;
  esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO=""
for candidate in \
  "$(cd "$SCRIPT_DIR/../../../.." && pwd)" \
  "$(cd "$SCRIPT_DIR/../../.." && pwd)"; do
  if [[ -f "$candidate/Makefile" && -f "$candidate/scripts/assemble-pack-ptbr.sh" ]]; then
    REPO="$candidate"
    break
  fi
done
[[ -n "$REPO" ]] || die "Raiz do repo não encontrada (esperado Makefile + scripts/assemble-pack-ptbr.sh)"

VERSION_FILE="$REPO/output/Translation/version.json"
DIST_DLL="$REPO/dist/PgTranslateLive.dll"
DIST_CFG="$REPO/dist/com.pg.translatelive.cfg"

pack_version() {
  python3 -c "import json; print(json.load(open('$VERSION_FILE'))['Version'])"
}

plugin_version() {
  grep -oP '(?<=\[BepInPlugin\("com.pg.translatelive", "Pg Translate Live", ")[^"]+' \
    "$REPO/bepinex-plugin/PgTranslateLive/src/main.cs" 2>/dev/null || echo "?"
}

echo "══════════════════════════════════════════════════════════════"
echo " REPACK — Project Gorgon PT-BR"
echo "══════════════════════════════════════════════════════════════"
echo ""
echo "Repo:   $REPO"
echo "Modo:   $MODE"
echo ""

if [[ "$MODE" == full ]]; then
  echo "Passos:"
  echo "  1. make write"
  echo "  2. make build-plugin sync-dist"
  echo "  3. make pack-ptbr"
  echo "  4. make release-pack-ptbr"
  echo "  5. make verify-pack-ptbr"
else
  echo "Passos:"
  echo "  1. make pack-ptbr          (sync-dist incluso no target)"
  echo "  2. make release-pack-ptbr"
  echo "  3. make verify-pack-ptbr"
fi
echo ""

if [[ -f "$VERSION_FILE" ]]; then
  echo "Pack version (version.json): $(pack_version)"
else
  echo "Pack version: version.json AUSENTE"
fi
echo "Plugin version (main.cs):    $(plugin_version)"
echo ""

if [[ "$DRY_RUN" -eq 1 ]]; then
  echo "[dry-run] Nenhum comando executado."
  exit 0
fi

cd "$REPO"

if [[ "$MODE" == full ]]; then
  make write
  make build-plugin sync-dist
else
  [[ -f "$VERSION_FILE" ]] || die "output/Translation/version.json ausente — rode make write ou use --full"
  [[ -f "$DIST_DLL" ]] || die "dist/PgTranslateLive.dll ausente — rode make build-plugin sync-dist ou use --full"
  [[ -f "$DIST_CFG" ]] || die "dist/com.pg.translatelive.cfg ausente"
fi

make pack-ptbr
make release-pack-ptbr
make verify-pack-ptbr

VER="$(pack_version)"
ZIP="$REPO/releases/Project-Gorgon-PT-BR-v${VER}-Pack.zip"

echo ""
echo "Pronto:"
echo "  pack/  → $REPO/pack/ptbr/"
echo "  zip    → $ZIP"
[[ -f "$ZIP" ]] && du -sh "$ZIP"
