#!/usr/bin/env bash
# Caminhos Steam / jogo — compartilhado por install.sh e verify-install.sh
# Defina GAME_DIR, STEAM_ROOT ou STEAM_APP_ID antes de source, se necessário.

STEAM_APP_ID_FULL=342940
STEAM_APP_ID_DEMO=969170
PG_PROTON_SUFFIX="pfx/drive_c/users/steamuser/AppData/LocalLow/Elder Game/Project Gorgon"

_find_game_dir() {
  if [[ -n "${GAME_DIR:-}" ]]; then
    if [[ -d "$GAME_DIR" ]]; then
      (cd "$GAME_DIR" && pwd)
      return
    fi
    echo "Erro: GAME_DIR=$GAME_DIR não existe" >&2
    return 1
  fi

  local candidate
  for candidate in \
    "$HOME/.steam/debian-installation/steamapps/common/Project Gorgon" \
    "$HOME/.steam/steam/steamapps/common/Project Gorgon" \
    "$HOME/.local/share/Steam/steamapps/common/Project Gorgon" \
    "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Project Gorgon"; do
    if [[ -d "$candidate" ]]; then
      (cd "$candidate" && pwd)
      return
    fi
  done
  return 1
}

_find_steam_root() {
  if [[ -n "${STEAM_ROOT:-}" && -d "$STEAM_ROOT" ]]; then
    echo "$STEAM_ROOT"
    return
  fi
  if [[ -n "${GAME_DIR:-}" ]]; then
    local root
    root="$(echo "$GAME_DIR" | sed 's|/steamapps/common/Project Gorgon$||')"
    if [[ -d "$root/steamapps" ]]; then
      echo "$root"
      return
    fi
  fi
  for candidate in \
    "$HOME/.steam/debian-installation" \
    "$HOME/.steam/steam" \
    "$HOME/.local/share/Steam" \
    "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam"; do
    if [[ -d "$candidate/steamapps" ]]; then
      echo "$candidate"
      return
    fi
  done
  echo "$HOME/.steam/debian-installation"
}

_proton_prefix_for_app() {
  echo "$STEAM_ROOT/steamapps/compatdata/$1/$PG_PROTON_SUFFIX"
}

_proton_label_for_app() {
  case "$1" in
    "$STEAM_APP_ID_FULL") echo "pago (342940)" ;;
    "$STEAM_APP_ID_DEMO") echo "demo (969170)" ;;
    *) echo "app $1" ;;
  esac
}

_resolve_steam_app_ids() {
  if [[ -n "${STEAM_APP_ID:-}" ]]; then
    echo "$STEAM_APP_ID"
    return
  fi
  # Pasta Translation/ única — Project Gorgon pago (342940).
  echo "$STEAM_APP_ID_FULL"
}

# Copia pack CDN para Translation/ preservando strings_npctalk.json do jogo se já existir.
copy_translation_pack() {
  local src="$1" dest="$2"
  local f base
  mkdir -p "$dest"
  for f in "$src"/*; do
    [[ -e "$f" ]] || continue
    base="$(basename "$f")"
    if [[ "$base" == "strings_npctalk.json" && -f "$dest/strings_npctalk.json" ]]; then
      echo "  preservando strings_npctalk.json existente ($dest)"
      continue
    fi
    cp -a "$f" "$dest/"
  done
}

_init_proton_paths() {
  STEAM_APP_IDS=()
  PROTON_PREFIXES=()
  PROTON_LABELS=()

  local app_id
  while IFS= read -r app_id; do
    [[ -z "$app_id" ]] && continue
    STEAM_APP_IDS+=("$app_id")
    PROTON_PREFIXES+=("$(_proton_prefix_for_app "$app_id")")
    PROTON_LABELS+=("$(_proton_label_for_app "$app_id")")
  done < <(_resolve_steam_app_ids)

  if [[ -z "${PROTON_PREFIX:-}" && ${#PROTON_PREFIXES[@]} -gt 0 ]]; then
    PROTON_PREFIX="${PROTON_PREFIXES[0]}"
  fi
}

GAME_DIR="$(_find_game_dir 2>/dev/null || true)"
STEAM_ROOT="$(_find_steam_root)"
_init_proton_paths
UNITY_LINUX="${UNITY_LINUX:-$HOME/.config/unity3d/Elder Game/Project Gorgon}"
CANONICAL_TRANSLATION_DIR="${STEAM_ROOT}/steamapps/compatdata/${STEAM_APP_ID_FULL}/${PG_PROTON_SUFFIX}/Translation"
