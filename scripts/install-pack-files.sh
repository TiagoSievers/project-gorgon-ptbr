#!/usr/bin/env bash
# Instalação canônica do pack PT-BR (mesma lógica que Instalador-Linux).
# Uso: install-pack-files.sh --linux|--windows PTBR_ROOT GAME_DIR
set -euo pipefail

OS=""
PTBR_ROOT=""
GAME_DIR=""
DRY_RUN=0

die() { echo "ERRO: $*" >&2; exit 1; }

progress_emit() {
  local pct="$1"
  local msg="$2"
  [[ -n "${PG_PROGRESS_FD:-}" ]] || return 0
  printf '%s\n# %s\n' "$pct" "$msg" >&"${PG_PROGRESS_FD}" 2>/dev/null || true
}

usage() {
  cat <<EOF
Uso: install-pack-files.sh --linux|--windows PTBR_ROOT GAME_DIR

  PTBR_ROOT   pasta ptbr/ (para-pasta-do-jogo/ + para-Translation/)
  GAME_DIR    pasta Project Gorgon na Steam
EOF
  exit 1
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --linux) OS=linux; shift ;;
    --windows) OS=windows; shift ;;
    --dry-run) DRY_RUN=1; shift ;;
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

[[ -n "$OS" && -n "$PTBR_ROOT" && -n "$GAME_DIR" ]] || usage

PTBR_ROOT="$(cd "$PTBR_ROOT" && pwd)"
GAME_DIR="$(cd "$GAME_DIR" && pwd)"
GAME_SRC="$PTBR_ROOT/para-pasta-do-jogo"
TRANS_SRC="$PTBR_ROOT/para-Translation"

[[ -d "$GAME_SRC" ]] || die "Não encontrado: $GAME_SRC"
[[ -f "$TRANS_SRC/version.json" ]] || die "Não encontrado: $TRANS_SRC/version.json"
[[ -f "$GAME_SRC/BepInEx/plugins/PgTranslateLive/PgTranslateLive.dll" ]] || \
  die "Plugin ausente em $GAME_SRC/BepInEx/plugins/PgTranslateLive/"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=install-paths.sh
source "$SCRIPT_DIR/install-paths.sh"

resolve_apply_logging() {
  local candidate
  for candidate in \
    "$PTBR_ROOT/scripts/apply-bepinex-minimal-logging.sh" \
    "$SCRIPT_DIR/apply-bepinex-minimal-logging.sh"; do
    if [[ -x "$candidate" ]]; then
      echo "$candidate"
      return 0
    fi
  done
  return 1
}

declare -a TRANS_DESTS=()
declare -a TRANS_LABELS=()

if [[ "$OS" == windows ]]; then
  local_low="${LOCALAPPDATA:-}"
  if [[ -z "$local_low" && -d "/mnt/c/Users" ]]; then
    user="$(ls /mnt/c/Users 2>/dev/null | grep -vE '^(All Users|Default|Public|desktop\.ini)$' | head -1 || true)"
    [[ -n "$user" ]] && local_low="/mnt/c/Users/$user/AppData/LocalLow"
  fi
  [[ -n "$local_low" ]] || die "Não foi possível resolver AppData\\LocalLow."
  TRANS_DESTS+=("$local_low/Elder Game/Project Gorgon/Translation")
  TRANS_LABELS+=("Windows LocalLow")
else
  TRANS_DESTS+=("$CANONICAL_TRANSLATION_DIR")
  TRANS_LABELS+=("Proton 342940 (único)")
fi

if [[ "$DRY_RUN" -eq 1 ]]; then
  echo "ORIGEM jogo: $GAME_SRC/"
  echo "DESTINO jogo: $GAME_DIR/"
  echo "ORIGEM tradução: $TRANS_SRC/"
  echo "Extras: BepInEx log mínimo + remover Translator legado"
  for i in "${!TRANS_DESTS[@]}"; do
    echo "  → ${TRANS_DESTS[$i]}  (${TRANS_LABELS[$i]})"
  done
  echo "[dry-run] Nenhum arquivo copiado."
  exit 0
fi

progress_emit 5 "Iniciando instalação…"
progress_emit 15 "Copiando BepInEx + plugin para pasta do jogo…"
cp -a "$GAME_SRC/." "$GAME_DIR/"

if apply_script="$(resolve_apply_logging)"; then
  progress_emit 25 "Ajustando BepInEx (log mínimo)…"
  bash "$apply_script" "$GAME_DIR"
fi
progress_emit 45 "BepInEx + plugin instalados"

pct=55
for i in "${!TRANS_DESTS[@]}"; do
  dest="${TRANS_DESTS[$i]}"
  progress_emit "$pct" "Copiando language pack (${TRANS_LABELS[$i]})…"
  copy_translation_pack "$TRANS_SRC" "$dest"
  pct=$((pct + 8))
done

legacy="$GAME_DIR/BepInEx/plugins/Translator"
if [[ -d "$legacy" ]]; then
  progress_emit 88 "Removendo mod legado Translator…"
  rm -rf "$legacy"
fi

progress_emit 96 "Verificando arquivos…"
[[ -f "$GAME_DIR/BepInEx/plugins/PgTranslateLive/PgTranslateLive.dll" ]] || \
  die "Plugin não encontrado após instalação."
ok_trans=0
for dest in "${TRANS_DESTS[@]}"; do
  [[ -f "$dest/version.json" ]] && ok_trans=1
done
[[ "$ok_trans" -eq 1 ]] || die "version.json não encontrado em Translation/."

progress_emit 100 "Instalação concluída!"
