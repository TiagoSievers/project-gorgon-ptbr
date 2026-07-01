#!/usr/bin/env bash
# CLI da skill / agente — delega para install-pack-files.sh (mesmo que Instalador-Linux).
# Uso: install-directories.sh --linux|--windows PTBR_ROOT [GAME_DIR]
#   --dry-run   mostra plano sem copiar
#   --yes       pula confirmação interativa (agente já confirmou)
set -euo pipefail

OS=""
PTBR_ROOT=""
GAME_DIR=""
DRY_RUN=0
ASSUME_YES=0

die() { echo "ERRO: $*" >&2; exit 1; }

usage() {
  cat <<EOF
Uso: install-directories.sh --linux|--windows PTBR_ROOT [GAME_DIR]

  Mesma instalação que Instalador-Linux / Instalador-Windows.exe (install-pack-files.sh).

Opções:
  --dry-run   mostra o que seria copiado
  --yes       não pede confirmação no terminal
EOF
  exit 1
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --linux) OS=linux; shift ;;
    --windows) OS=windows; shift ;;
    --dry-run) DRY_RUN=1; shift ;;
    --yes) ASSUME_YES=1; shift ;;
    -h|--help) usage ;;
    *)
      if [[ -z "$PTBR_ROOT" ]]; then PTBR_ROOT="$1"
      elif [[ -z "$GAME_DIR" ]]; then GAME_DIR="$1"
      else die "argumento inesperado: $1"
      fi
      shift
      ;;
  esac
done

[[ -n "$OS" ]] || usage
[[ -n "$PTBR_ROOT" ]] || usage

PTBR_ROOT="$(cd "$PTBR_ROOT" && pwd)"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CORE=""
for candidate in \
  "$SCRIPT_DIR/install-pack-files.sh" \
  "$(cd "$SCRIPT_DIR/../../../.." && pwd)/scripts/install-pack-files.sh" \
  "$(cd "$SCRIPT_DIR/../../.." && pwd)/scripts/install-pack-files.sh" \
  "$(cd "$SCRIPT_DIR/../.." && pwd)/scripts/install-pack-files.sh"; do
  if [[ -x "$candidate" ]]; then
    CORE="$candidate"
    break
  fi
done
[[ -n "$CORE" ]] || die "install-pack-files.sh não encontrado"

detect_game_linux() {
  local c
  for c in \
    "$HOME/.steam/debian-installation/steamapps/common/Project Gorgon" \
    "$HOME/.steam/steam/steamapps/common/Project Gorgon" \
    "$HOME/.local/share/Steam/steamapps/common/Project Gorgon"; do
    [[ -d "$c" ]] && { echo "$c"; return 0; }
  done
  return 1
}

detect_game_windows() {
  local c
  for c in \
    "/mnt/c/Program Files (x86)/Steam/steamapps/common/Project Gorgon" \
    "/mnt/c/Program Files/Steam/steamapps/common/Project Gorgon"; do
    [[ -d "$c" ]] && { echo "$c"; return 0; }
  done
  return 1
}

if [[ -z "$GAME_DIR" ]]; then
  if [[ "$OS" == linux ]]; then
    GAME_DIR="$(detect_game_linux || true)"
  else
    GAME_DIR="$(detect_game_windows || true)"
  fi
fi

[[ -n "$GAME_DIR" ]] || die "GAME_DIR não informado e não foi detectado automaticamente."
[[ -d "$GAME_DIR" ]] || die "Pasta do jogo não existe: $GAME_DIR"

echo "══════════════════════════════════════════════════════════════"
echo " PLANO — Project Gorgon PT-BR (mesmo que Instalador-Linux)"
echo "══════════════════════════════════════════════════════════════"
echo ""
bash "$CORE" "--$OS" --dry-run "$PTBR_ROOT" "$GAME_DIR"
echo ""

if [[ "$DRY_RUN" -eq 1 ]]; then
  exit 0
fi

if [[ "$ASSUME_YES" -eq 0 ]]; then
  read -r -p "Continuar com a instalação? [s/N] " ans
  [[ "${ans,,}" == "s" || "${ans,,}" == "sim" ]] || { echo "Cancelado."; exit 0; }
fi

bash "$CORE" "--$OS" "$PTBR_ROOT" "$GAME_DIR"

echo ""
echo "Plugin: $GAME_DIR/BepInEx/plugins/PgTranslateLive/PgTranslateLive.dll"
echo "Log:    $GAME_DIR/BepInEx/LogOutput.log"
echo ""

if [[ "$OS" == linux ]]; then
  echo "Steam Launch Options:"
  echo '  WINEDLLOVERRIDES="winhttp.dll=n,b" %command%'
  echo "Proton 9 ou Experimental."
fi
