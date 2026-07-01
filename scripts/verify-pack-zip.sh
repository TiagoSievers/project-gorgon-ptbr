#!/usr/bin/env bash
# Extrai um zip de release e verifica arquivos obrigatórios.
# Uso: verify-pack-zip.sh [caminho/do.zip]
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ZIP="${1:-}"
VERSION_FILE="$ROOT/output/Translation/version.json"

die() { echo "ERRO: $*" >&2; exit 1; }
ok() { echo "  OK  $*"; }
miss() { echo "  FALTA  $*"; FAIL=1; }

FAIL=0

if [[ -z "$ZIP" ]]; then
  [[ -f "$VERSION_FILE" ]] || die "version.json ausente"
  VER="$(python3 -c "import json; print(json.load(open('$VERSION_FILE'))['Version'])")"
  ZIP="$ROOT/releases/Project-Gorgon-PT-BR-v${VER}-Pack.zip"
fi

[[ -f "$ZIP" ]] || die "Zip não encontrado: $ZIP"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

echo "==> Zip: $ZIP"
echo "==> Extraindo em: $WORK"
unzip -q "$ZIP" -d "$WORK"

# Descobre raiz do pack (ptbr/ ou pg-ptbr/)
PACK_ROOT=""
for candidate in "$WORK"/ptbr "$WORK"/pg-ptbr; do
  if [[ -d "$candidate" ]]; then
    PACK_ROOT="$candidate"
    break
  fi
done
[[ -n "$PACK_ROOT" ]] || die "Raiz do pack não encontrada (esperado ptbr/ ou pg-ptbr/)"

echo ""
echo "==> Raiz: $(basename "$PACK_ROOT")/"
echo "==> Verificando arquivos obrigatórios…"

check() {
  local rel="$1"
  if [[ -e "$PACK_ROOT/$rel" ]]; then
    ok "$rel"
  else
    miss "$rel"
  fi
}

check "Instalador-Windows.exe"
check "Instalador-Linux"
check "LEIA-ME.txt"
check "IA_instruction.md"
check "SKILL.md"
check "DESINSTALAR-SKILL.md"
check "scripts/install-directories.sh"
check "scripts/uninstall-directories.sh"
check "scripts/uninstall.sh"
check "scripts/install-paths.sh"
check "scripts/install-pack-files.sh"
check "scripts/apply-bepinex-minimal-logging.sh"
check "VERSAO.txt"
check "para-pasta-do-jogo/winhttp.dll"
check "para-pasta-do-jogo/doorstop_config.ini"
check "para-pasta-do-jogo/BepInEx/plugins/PgTranslateLive/PgTranslateLive.dll"
check "para-pasta-do-jogo/BepInEx/config/com.pg.translatelive.cfg"
check "para-Translation/version.json"

echo ""
echo "==> Atalhos antigos (nao devem existir)"
for legacy in INSTALAR INSTALAR.sh INSTALAR.exe instalador-linux-worker.sh Instalador-Linux.desktop; do
  if [[ -e "$PACK_ROOT/$legacy" ]]; then
    echo "  SOBROU  $legacy (remover do pack)"
    FAIL=1
  else
    echo "  OK  ausente: $legacy"
  fi
done

echo ""
echo "==> Contagem de arquivos"
find "$PACK_ROOT" -type f | wc -l | xargs echo "  Total arquivos:"
du -sh "$PACK_ROOT" | awk '{print "  Tamanho:", $1}'

echo ""
echo "==> Top-level"
ls -la "$PACK_ROOT"

echo ""
if [[ "$FAIL" -eq 0 ]]; then
  echo "VERIFICACAO: OK — todos os arquivos obrigatórios presentes."
  exit 0
else
  echo "VERIFICACAO: FALHOU — itens ausentes acima."
  exit 1
fi
