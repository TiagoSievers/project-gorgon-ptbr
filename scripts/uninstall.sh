#!/usr/bin/env bash
# Remove BepInEx, plugins PT-BR e language pack instalados pelo pacote.
# Uso: ./uninstall.sh              (na raiz do repo ou do pacote Release)
#      GAME_DIR=... ./uninstall.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# shellcheck source=install-paths.sh
source "$SCRIPT_DIR/install-paths.sh"

die() { echo "Erro: $*" >&2; exit 1; }
info() { echo "==> $*"; }
ok() { echo "  OK   $*"; }
missing() { echo "  OK   já ausente: $*"; }

progress_emit() {
  local pct="$1"
  local msg="$2"
  [[ -n "${PG_PROGRESS_FD:-}" ]] || return 0
  printf '%s\n# %s\n' "$pct" "$msg" >&"${PG_PROGRESS_FD}" 2>/dev/null || true
}

usage() {
  cat <<EOF
Project Gorgon PT-BR — desinstalador

Uso:
  ./uninstall.sh              Remove BepInEx + plugin + language pack PT-BR
  ./uninstall.sh --help

Variáveis:
  GAME_DIR=...     Pasta do jogo (auto-detecta Steam Linux / Flatpak)
  STEAM_ROOT=...   Raiz da Steam (inferido de GAME_DIR se omitido)
  STEAM_APP_ID=... Só um prefix Proton (padrão: pago 342940 + demo 969170)
EOF
}

require_game() {
  GAME_DIR="$(_find_game_dir)" || die \
    "Project Gorgon não encontrado. Instale na Steam ou defina:\n  GAME_DIR=/caminho/para/Project\\ Gorgon"
  STEAM_ROOT="$(_find_steam_root)"
  _init_proton_paths
}

remove_path() {
  local path="$1"
  local label="$2"
  if [[ -e "$path" ]]; then
    rm -rf "$path"
    ok "removido: $label ($path)"
  else
    missing "$label"
  fi
}

uninstall_game_files() {
  progress_emit 25 "Removendo BepInEx e plugins…"
  remove_path "$GAME_DIR/BepInEx" "BepInEx (plugins e config)"
  remove_path "$GAME_DIR/dotnet" "dotnet (BepInEx CoreCLR)"
  remove_path "$GAME_DIR/winhttp.dll" "winhttp.dll (Doorstop)"
  remove_path "$GAME_DIR/doorstop_config.ini" "doorstop_config.ini"
  remove_path "$GAME_DIR/.doorstop_version" ".doorstop_version"
}

uninstall_translation_packs() {
  local i prefix label pct count
  count="${#PROTON_PREFIXES[@]}"
  progress_emit 55 "Removendo language pack PT-BR…"
  remove_path "$UNITY_LINUX/Translation" "Language pack Linux nativo"
  for i in "${!PROTON_PREFIXES[@]}"; do
    prefix="${PROTON_PREFIXES[$i]}"
    label="${PROTON_LABELS[$i]}"
    pct=$((60 + (i + 1) * 20 / count))
    progress_emit "$pct" "Language pack Proton $label…"
    remove_path "$prefix/Translation" "Language pack Proton $label"
  done
}

verify_uninstall() {
  local fail=0
  progress_emit 90 "Verificando desinstalação…"
  [[ -f "$GAME_DIR/winhttp.dll" ]] && { echo "  FALHA winhttp.dll ainda presente" >&2; fail=1; }
  [[ -f "$GAME_DIR/BepInEx/plugins/PgTranslateLive/PgTranslateLive.dll" ]] && {
    echo "  FALHA PgTranslateLive ainda presente" >&2
    fail=1
  }
  [[ -f "$UNITY_LINUX/Translation/version.json" ]] && {
    echo "  FALHA language pack Linux ainda presente" >&2
    fail=1
  }
  local i prefix
  for i in "${!PROTON_PREFIXES[@]}"; do
    prefix="${PROTON_PREFIXES[$i]}"
    if [[ -f "$prefix/Translation/version.json" ]]; then
      echo "  FALHA language pack Proton ${PROTON_LABELS[$i]} ainda presente" >&2
      fail=1
    fi
  done
  [[ "$fail" -eq 0 ]]
}

print_removed_paths() {
  local i prefix label proton_lines=""
  for i in "${!PROTON_PREFIXES[@]}"; do
    prefix="${PROTON_PREFIXES[$i]}"
    label="${PROTON_LABELS[$i]}"
    proton_lines+="Language pack PT-BR (Proton $label):
  $prefix/Translation/

"
  done

  cat <<EOF

==============================================
 REMOVIDO (caminhos do pacote PT-BR)
==============================================

Jogo (Steam):
  $GAME_DIR

BepInEx / Doorstop:
  $GAME_DIR/BepInEx/
  $GAME_DIR/dotnet/
  $GAME_DIR/winhttp.dll
  $GAME_DIR/doorstop_config.ini
  $GAME_DIR/.doorstop_version

${proton_lines}Language pack PT-BR (Linux nativo):
  $UNITY_LINUX/Translation/

==============================================
EOF
}

print_finish() {
  echo ""
  echo "=============================================="
  echo " DESINSTALAÇÃO CONCLUÍDA"
  echo "=============================================="
  print_removed_paths
  echo ""
  echo "O jogo volta ao inglês original."
  echo "Abra o Project Gorgon pela Steam normalmente."
  echo "=============================================="
}

main() {
  case "${1:-}" in
    -h|--help)
      usage
      exit 0
      ;;
  esac

  info "Project Gorgon PT-BR — desinstalador"
  info "Origem: $ROOT"
  progress_emit 5 "Iniciando desinstalação…"
  require_game
  info "Jogo: $GAME_DIR"
  progress_emit 15 "Jogo encontrado"
  uninstall_game_files
  uninstall_translation_packs
  progress_emit 92 "Verificando…"
  if verify_uninstall; then
    progress_emit 100 "Desinstalação concluída!"
    print_finish
    echo ""
    echo ">>> Mod PT-BR removido."
  else
    progress_emit 100 "Desinstalação concluída (com avisos)"
    print_finish
    echo ""
    echo "Alguns arquivos não puderam ser removidos — veja acima."
    exit 1
  fi
}

main "$@"
