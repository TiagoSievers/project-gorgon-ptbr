#!/usr/bin/env bash
# CLI da skill — remove pack PT-BR instalado (delega para scripts/uninstall.sh).
# Uso: uninstall-directories.sh [--yes] [GAME_DIR]
set -euo pipefail

ASSUME_YES=0
GAME_DIR_ARG=""

die() { echo "ERRO: $*" >&2; exit 1; }

usage() {
  cat <<EOF
Uso: uninstall-directories.sh [--yes] [GAME_DIR]

Remove BepInEx, PgTranslateLive e Translation/ PT-BR (Proton + Linux nativo).

Opções:
  --yes   não pede confirmação (agente já confirmou)
EOF
  exit 1
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --yes) ASSUME_YES=1; shift ;;
    -h|--help) usage ;;
    *)
      [[ -z "$GAME_DIR_ARG" ]] || die "argumento inesperado: $1"
      GAME_DIR_ARG="$1"
      shift
      ;;
  esac
done

find_uninstall_sh() {
  local here dir
  here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
  if [[ -f "$here/uninstall.sh" ]]; then
    echo "$here/uninstall.sh"
    return 0
  fi
  for dir in \
    "$here/../../../.." \
    "$here/../../.." \
    "$here/../.." \
    "$here/.."; do
    if [[ -f "$dir/scripts/uninstall.sh" ]]; then
      echo "$dir/scripts/uninstall.sh"
      return 0
    fi
  done
  return 1
}

UNINSTALL="$(find_uninstall_sh)" || die "scripts/uninstall.sh não encontrado (repo ou pack incompleto)"

if [[ "$ASSUME_YES" -eq 0 ]]; then
  echo "Desinstalar mod PT-BR? (BepInEx + plugin + Translation/)"
  read -r -p "Digite sim para continuar: " confirm
  [[ "$confirm" == "sim" ]] || { echo "Cancelado."; exit 0; }
fi

export GAME_DIR="${GAME_DIR_ARG:-${GAME_DIR:-}}"
exec bash "$UNINSTALL"
