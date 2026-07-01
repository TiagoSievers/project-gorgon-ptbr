#!/usr/bin/env bash
# Instalador PT-BR — Linux (GUI zenity + cópia). Um único arquivo, como Instalador-Windows.exe.
set -euo pipefail

ROOT="$(cd "$(dirname "$(readlink -f "${BASH_SOURCE[0]}" 2>/dev/null || echo "${BASH_SOURCE[0]}")")" && pwd)"
cd "$ROOT"

GAME_SRC="$ROOT/para-pasta-do-jogo"
TRANS_SRC="$ROOT/para-Translation"

die_gui() {
  zenity --error --width=480 --title="Project Gorgon PT-BR" --text="$1" 2>/dev/null || {
    echo "Erro: $1" >&2
    read -r -p "Enter para sair…"
  }
  exit 1
}

need_zenity() {
  command -v zenity >/dev/null 2>&1 || die_gui "Instale zenity (interface gráfica):\n  sudo apt install zenity"
}

_tilde() {
  local p="$1"
  if [[ "$p" == "$HOME/"* ]]; then
    printf '~/%s' "${p#"$HOME/"}"
  else
    printf '%s' "$p"
  fi
}

progress_emit() {
  local pct="$1"
  local msg="$2"
  [[ -n "${PG_PROGRESS_FD:-}" ]] || return 0
  printf '%s\n# %s\n' "$pct" "$msg" >&"${PG_PROGRESS_FD}" 2>/dev/null || true
}

check_pack() {
  local missing=""
  [[ -d "$GAME_SRC" ]] || missing+="para-pasta-do-jogo/\n"
  [[ -f "$TRANS_SRC/version.json" ]] || missing+="para-Translation/version.json\n"
  [[ -f "$GAME_SRC/BepInEx/plugins/PgTranslateLive/PgTranslateLive.dll" ]] || \
    missing+="para-pasta-do-jogo/BepInEx/plugins/PgTranslateLive/PgTranslateLive.dll\n"
  [[ -z "$missing" ]] || die_gui "Pacote incompleto.\n\nExtraia o .zip inteiro e abra a pasta ptbr/.\n\nFalta:\n$missing"
}

detect_game() {
  local c
  for c in \
    "$HOME/.steam/debian-installation/steamapps/common/Project Gorgon" \
    "$HOME/.steam/steam/steamapps/common/Project Gorgon" \
    "$HOME/.local/share/Steam/steamapps/common/Project Gorgon"; do
    if [[ -d "$c" ]]; then
      echo "$c"
      return
    fi
  done
  return 1
}

find_steam_root() {
  local g="$1"
  if [[ "$g" == *"/steamapps/common/"* ]]; then
    echo "${g%/steamapps/common/Project Gorgon}"
    return
  fi
  echo "$HOME/.steam/debian-installation"
}

build_destinations() {
  local game_dir="$1"
  local steam_root
  steam_root="$(find_steam_root "$game_dir")"
  GAME_PLAN="$game_dir"
  TRANS_LINUX="$HOME/.config/unity3d/Elder Game/Project Gorgon/Translation"
  TRANS_PROTON=()
  TRANS_LABELS=()
  local app suffix dest
  suffix="pfx/drive_c/users/steamuser/AppData/LocalLow/Elder Game/Project Gorgon/Translation"
  for app in 342940 969170; do
    dest="$steam_root/steamapps/compatdata/$app/$suffix"
    if [[ -d "$steam_root/steamapps/compatdata/$app" ]]; then
      TRANS_PROTON+=("$dest")
      case "$app" in
        342940) TRANS_LABELS+=("Proton pago (342940)") ;;
        969170) TRANS_LABELS+=("Proton demo (969170)") ;;
      esac
    fi
  done
}

build_plan_text() {
  local i
  cat <<EOF
Os arquivos abaixo serão copiados (merge — arquivos existentes serão substituídos):

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
ORIGEM: para-pasta-do-jogo/
DESTINO: $(_tilde "$GAME_PLAN")/

  • BepInEx/ (loader + plugin)
  • winhttp.dll, doorstop_config.ini, dotnet/
  • BepInEx/plugins/PgTranslateLive/PgTranslateLive.dll
  • BepInEx/config/com.pg.translatelive.cfg

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
ORIGEM: para-Translation/
DESTINOS:
EOF
  echo "  • $(_tilde "$TRANS_LINUX")  (Linux nativo)"
  for i in "${!TRANS_PROTON[@]}"; do
    echo "  • $(_tilde "${TRANS_PROTON[$i]}")  (${TRANS_LABELS[$i]})"
  done
  cat <<'EOF'

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Clique em Continuar para instalar ou Cancelar para sair.
EOF
}

install_files() {
  local game="$1"
  local core="$ROOT/scripts/install-pack-files.sh"
  [[ -x "$core" ]] || die_gui "Pacote incompleto.\n\nFalta: scripts/install-pack-files.sh"
  bash "$core" --linux "$ROOT" "$game"
}

run_install() {
  local game_dir="$1"
  local log progress_fifo
  log="$(mktemp /tmp/pg-ptbr-pack-install.XXXXXX.log)"
  progress_fifo="$(mktemp -u /tmp/pg-ptbr-pack-progress.XXXXXX)"
  mkfifo "$progress_fifo"

  zenity --progress --percentage=0 --auto-close --no-cancel \
    --title="Instalando PT-BR…" --width=520 \
    < "$progress_fifo" 2>/dev/null &
  local zpid=$!
  sleep 0.2

  (
    export PG_PROGRESS_FD=3
    exec 3>"$progress_fifo"
    trap 'exec 3>&-' EXIT
    install_files "$game_dir"
  ) >"$log" 2>&1 &
  local pid=$!

  local st=0
  wait "$pid" || st=$?
  wait "$zpid" 2>/dev/null || true
  rm -f "$progress_fifo"

  if [[ "$st" -eq 0 ]]; then
    local done_msg i
    done_msg="Instalação concluída!\n\nOs arquivos estão em:\n\n"
    done_msg+="Jogo + plugin:\n$(_tilde "$game_dir")\n\n"
    done_msg+="Tradução CDN:\n$(_tilde "$TRANS_LINUX")\n"
    for i in "${!TRANS_PROTON[@]}"; do
      done_msg+="$(_tilde "${TRANS_PROTON[$i]}")  (${TRANS_LABELS[$i]})\n"
    done
    done_msg+="\nLaunch Options (Steam → Proton):\n  WINEDLLOVERRIDES=\"winhttp.dll=n,b\" %command%\n\n"
    done_msg+="Log do plugin:\n$(_tilde "$game_dir")/BepInEx/LogOutput.log"

    zenity --info --width=640 --title="Instalação concluída" \
      --ok-label="Fechar" --text="$done_msg"
    return 0
  fi

  zenity --text-info --title="Falha na instalação" --filename="$log" \
    --width=720 --height=420 --ok-label="Fechar"
  rm -f "$log"
  return 1
}

main() {
  need_zenity
  check_pack

  local detected game_dir
  detected="$(detect_game || true)"

  if [[ -n "$detected" ]]; then
    game_dir="$(zenity --entry \
      --title="Pasta do jogo" \
      --text="Confirme o caminho do Project Gorgon na Steam:" \
      --entry-text="$detected" 2>/dev/null)" || exit 0
  else
    game_dir="$(zenity --file-selection --directory \
      --title="Selecione a pasta Project Gorgon (Steam)" \
      --filename="$HOME/" 2>/dev/null)" || exit 0
  fi

  [[ -d "$game_dir" ]] || die_gui "Pasta não encontrada:\n$game_dir"

  build_destinations "$game_dir"
  local plan
  plan="$(build_plan_text)"

  zenity --question --width=640 --height=420 \
    --title="Project Gorgon PT-BR — Confirmar instalação" \
    --ok-label="Continuar" --cancel-label="Cancelar" \
    --text="$plan" || exit 0

  run_install "$game_dir"
}

main "$@"
