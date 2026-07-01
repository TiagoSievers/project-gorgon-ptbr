#!/usr/bin/env bash
# Ajusta BepInEx/config/BepInEx.cfg para log mínimo (evita stutter com UnityLog + console).
# Uso: scripts/apply-bepinex-minimal-logging.sh [GAME_DIR]
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

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
    line = "LogLevels = Error, Warning"
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

info "BepInEx log monitor aplicado: $CFG"
info "  UnityLogListening = false"
info "  Console Enabled = false"
info "  Disk LogLevels = All (LogOutput.log — tail/grep do plugin)"
info "  InstantFlushing = true"
info "  WriteUnityLog = false"
