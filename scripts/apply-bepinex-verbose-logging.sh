#!/usr/bin/env bash
# BepInEx log completo — necessário para ver TalkLog (LogInfo) do PgTranslateLive.
# Uso: scripts/apply-bepinex-verbose-logging.sh [GAME_DIR]
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

die() { echo "Erro: $*" >&2; exit 1; }
info() { echo "==> $*"; }

if [[ -n "${1:-}" ]]; then
  GAME_DIR="$1"
else
  # shellcheck source=install-paths.sh
  source "$SCRIPT_DIR/install-paths.sh"
  GAME_DIR="$(_find_game_dir)" || die "Project Gorgon não encontrado — passe GAME_DIR=..."
fi

CFG="$GAME_DIR/BepInEx/config/BepInEx.cfg"
[[ -f "$CFG" ]] || die "BepInEx.cfg não encontrado: $CFG"

tmp="$(mktemp)"
awk '
BEGIN { section = "" }
{
  line = $0
  sub(/\r$/, "", line)
  if (line ~ /^\[.*\]$/) {
    section = line
  }
  if (section == "[Logging]" && line ~ /^UnityLogListening = /) {
    line = "UnityLogListening = false"
  } else if (section == "[Logging.Console]" && line ~ /^Enabled = /) {
    line = "Enabled = false"
  } else if (section == "[Logging.Console]" && line ~ /^LogLevels = /) {
    line = "LogLevels = All"
  } else if (section == "[Logging.Disk]" && line ~ /^LogLevels = /) {
    line = "LogLevels = All"
  } else if (section == "[Logging.Disk]" && line ~ /^WriteUnityLog = /) {
    line = "WriteUnityLog = false"
  } else if (section == "[Logging.Disk]" && line ~ /^InstantFlushing = /) {
    line = "InstantFlushing = true"
  }
  print line
}
' "$CFG" > "$tmp"
cp "$tmp" "$CFG"
rm -f "$tmp"

info "BepInEx log verbose aplicado: $CFG"
info "  Disk LogLevels = All (LogOutput.log com Info do plugin)"
info "  InstantFlushing = true"
