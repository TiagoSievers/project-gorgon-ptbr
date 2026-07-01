#!/usr/bin/env bash
# Instalação completa: BepInEx + PgTranslateLive + language pack PT-BR
# Uso: ./install.sh              (na raiz do repo ou do pacote Release)
#      ./install.sh --verify     (só verifica instalação existente)
#      GAME_DIR=... ./install.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BEPINEX_URL="${BEPINEX_URL:-https://thunderstore.io/package/download/BepInEx/BepInExPack_IL2CPP/6.0.755/}"

# shellcheck source=install-paths.sh
source "$SCRIPT_DIR/install-paths.sh"

DIST_DLL="$ROOT/dist/PgTranslateLive.dll"
BUILD_DLL="$ROOT/bepinex-plugin/PgTranslateLive/bin/Release/net6.0/PgTranslateLive.dll"
DIST_CFG="$ROOT/dist/com.pg.translatelive.cfg"
PACK_SRC="$ROOT/output/Translation"
MOD_DL="$ROOT/.cache/mod-downloads"

LAUNCH_MIN='WINEDLLOVERRIDES="winhttp.dll=n,b" %command%'
LAUNCH_NVIDIA='env __NV_PRIME_RENDER_OFFLOAD=1 DXVK_FILTER_DEVICE_NAME="RTX 3050" WINEDLLOVERRIDES="winhttp.dll=n,b" %command%'

die() { echo "Erro: $*" >&2; exit 1; }
info() { echo "==> $*"; }

progress_emit() {
  local pct="$1"
  local msg="$2"
  [[ -n "${PG_PROGRESS_FD:-}" ]] || return 0
  printf '%s\n# %s\n' "$pct" "$msg" >&"${PG_PROGRESS_FD}" 2>/dev/null || true
}

usage() {
  cat <<EOF
Project Gorgon PT-BR — instalador

Uso:
  ./install.sh              Instala BepInEx + plugin + language pack
  ./install.sh --verify     Verifica instalação no jogo
  ./install.sh --help

Variáveis:
  GAME_DIR=...     Pasta do jogo (auto-detecta Steam Linux / Flatpak)
  STEAM_ROOT=...   Raiz da Steam (inferido de GAME_DIR se omitido)
  STEAM_APP_ID=... Só um prefix Proton (padrão: pago 342940 + demo 969170)

Requisitos: curl, unzip, Project Gorgon na Steam (pago ou demo).
BepInEx vem no pacote Release (vendor/); download só em fallback.
EOF
}

require_game() {
  GAME_DIR="$(_find_game_dir)" || die \
    "Project Gorgon não encontrado. Instale na Steam ou defina:\n  GAME_DIR=/caminho/para/Project\\ Gorgon"
  STEAM_ROOT="$(_find_steam_root)"
  _init_proton_paths
}

require_pack() {
  [[ -d "$PACK_SRC" && -f "$PACK_SRC/version.json" ]] || die \
    "output/Translation/ não encontrado em $ROOT (pack PT-BR ausente)"
}

require_tools() {
  command -v unzip >/dev/null 2>&1 || die "unzip necessário (ex.: sudo apt install unzip)"
}

resolve_plugin_dll() {
  if [[ -f "$DIST_DLL" ]]; then
    echo "$DIST_DLL"
    return
  fi
  if [[ -f "$BUILD_DLL" ]]; then
    echo "$BUILD_DLL"
    return
  fi
  die "PgTranslateLive.dll não encontrado em dist/ — pacote Release incompleto"
}

install_bepinex() {
  progress_emit 28 "Verificando BepInEx…"
  if [[ -f "$GAME_DIR/winhttp.dll" && -f "$GAME_DIR/BepInEx/core/BepInEx.Unity.IL2CPP.dll" ]]; then
    info "BepInEx já instalado"
    progress_emit 52 "BepInEx já instalado"
    return
  fi

  command -v unzip >/dev/null 2>&1 || die "unzip necessário (ex.: sudo apt install unzip)"

  local zip="" staging="$MOD_DL/bepinex-pack"
  local bundled="$ROOT/vendor/BepInExPack_IL2CPP.zip"

  if [[ -f "$bundled" ]]; then
    zip="$bundled"
    info "Instalando BepInEx IL2CPP (incluído no pacote)…"
    progress_emit 32 "Extraindo BepInEx (pacote local)…"
  else
    command -v curl >/dev/null 2>&1 || die \
      "BepInEx não incluído e curl ausente — use o pacote Release completo ou: make fetch-bepinex-vendor"
    mkdir -p "$MOD_DL"
    zip="$MOD_DL/BepInExPack_IL2CPP.zip"
    info "Baixando BepInEx IL2CPP (fallback internet)…"
    progress_emit 32 "Baixando BepInEx…"
    if ! curl -fsSL --retry 3 --retry-delay 2 "$BEPINEX_URL" -o "$zip"; then
      die "Falha ao baixar BepInEx.\nUse o pacote Release (vendor/ incluso) ou tente mais tarde."
    fi
  fi

  file "$zip" | grep -q 'Zip archive' || die "Arquivo BepInEx inválido: $zip"

  mkdir -p "$MOD_DL"
  rm -rf "$staging"
  progress_emit 38 "Descompactando BepInEx…"
  unzip -q -o "$zip" -d "$staging"

  progress_emit 44 "Copiando BepInEx para o jogo…"
  if [[ -d "$staging/BepInExPack" ]]; then
    cp -a "$staging/BepInExPack/." "$GAME_DIR/"
  else
    cp -a "$staging/." "$GAME_DIR/"
  fi

  [[ -f "$GAME_DIR/winhttp.dll" ]] || die "winhttp.dll não encontrado após instalar BepInEx"
  info "BepInEx instalado"
  progress_emit 52 "BepInEx instalado"
}

install_plugin() {
  progress_emit 58 "Instalando plugin PgTranslateLive…"
  local dll plugin_dir cfg_dir
  dll="$(resolve_plugin_dll)"
  plugin_dir="$GAME_DIR/BepInEx/plugins/PgTranslateLive"
  cfg_dir="$GAME_DIR/BepInEx/config"
  mkdir -p "$plugin_dir" "$cfg_dir"
  cp "$dll" "$plugin_dir/PgTranslateLive.dll"
  if [[ -f "$DIST_CFG" ]]; then
    cp "$DIST_CFG" "$cfg_dir/com.pg.translatelive.cfg"
  fi
  info "Plugin: $plugin_dir/PgTranslateLive.dll"
  progress_emit 64 "Plugin instalado"
}

remove_legacy_translator_plugin() {
  local legacy="$GAME_DIR/BepInEx/plugins/Translator"
  local legacy_cfg="$GAME_DIR/BepInEx/config/com.pickteam.translator.cfg"
  if [[ -d "$legacy" ]]; then
    rm -rf "$legacy"
    info "Removido legado: $legacy (tradução CDN fica só em Translation/)"
  fi
  if [[ -f "$legacy_cfg" ]]; then
    rm -f "$legacy_cfg"
    info "Removido legado: $legacy_cfg"
  fi
}

remove_legacy_npctalk_files() {
  local plugin_yaml="$GAME_DIR/BepInEx/plugins/PgTranslateLive/npcs.yaml"
  if [[ -f "$plugin_yaml" ]]; then
    rm -f "$plugin_yaml"
    info "Removido legado: $plugin_yaml"
  fi
}

remove_translation_legacy_yaml() {
  local yaml="$CANONICAL_TRANSLATION_DIR/npcs.yaml"
  if [[ -f "$yaml" ]]; then
    rm -f "$yaml"
    info "Removido legado: $yaml (use strings_npctalk.json)"
  fi
}

install_translation_pack() {
  progress_emit 66 "Copiando tradução CDN (language pack PT-BR)…"
  copy_translation_pack "$PACK_SRC" "$CANONICAL_TRANSLATION_DIR"
  info "Language pack (único): $CANONICAL_TRANSLATION_DIR"
  progress_emit 88 "Language pack instalado"
}

install_game_uninstaller() {
  local src="$ROOT/game-uninstall/uninstall-language-pack-ptbr"
  local dest="$GAME_DIR/uninstall-language-pack-ptbr"
  if [[ ! -f "$src" ]]; then
    die "game-uninstall/uninstall-language-pack-ptbr ausente no pacote"
  fi
  progress_emit 82 "Instalando desinstalador na pasta do jogo…"
  rm -f "$GAME_DIR/BepInEx/DESINSTALAR" "$GAME_DIR/BepInEx/DESINSTALAR.exe" 2>/dev/null || true
  cp "$src" "$dest"
  chmod +x "$dest"
  info "Desinstalador: $dest"
}

install_npctalk_json() {
  local src="$ROOT/output/Translation/strings_npctalk.json"
  local dest="$CANONICAL_TRANSLATION_DIR/strings_npctalk.json"
  if [[ ! -f "$src" ]]; then
    note "strings_npctalk.json ausente no pack — falas vêm do jogo ao jogar"
    return 0
  fi
  if [[ -f "$dest" ]]; then
    note "strings_npctalk.json do jogo preservado ($dest)"
    return 0
  fi
  mkdir -p "$CANONICAL_TRANSLATION_DIR"
  cp "$src" "$dest"
  info "strings_npctalk.json inicial: $dest"
}

print_installed_paths() {
  local plugin_dir="$GAME_DIR/BepInEx/plugins/PgTranslateLive/PgTranslateLive.dll"
  local cfg="$GAME_DIR/BepInEx/config/com.pg.translatelive.cfg"
  local log_plugin="$GAME_DIR/BepInEx/LogOutput.log"

  cat <<EOF

==============================================
 ONDE FOI INSTALADO (caminhos finais)
==============================================

Jogo (Steam):
  $GAME_DIR

BepInEx:
  $GAME_DIR/BepInEx/
  $GAME_DIR/winhttp.dll

Plugin PgTranslateLive (Falar ao vivo / Google):
  $plugin_dir

Config PgTranslateLive:
  $cfg

Tradução CDN + falas (único — plugin lê/grava aqui):
  $CANONICAL_TRANSLATION_DIR/

Log do plugin (após abrir o jogo):
  $log_plugin

==============================================
EOF
}

print_finish() {
  local ver=""
  if command -v python3 >/dev/null 2>&1; then
    ver="$(python3 -c "import json; print(json.load(open('$PACK_SRC/version.json'))['Version'])" 2>/dev/null || true)"
  fi
  echo ""
  echo "=============================================="
  echo " INSTALAÇÃO FINALIZADA COM SUCESSO"
  echo "=============================================="
  print_installed_paths
  echo ""
  echo "Próximos passos:"
  echo ""
  echo "1. Steam → Project Gorgon → Propriedades → Compatibilidade"
  echo "   → Forçar Steam Play → Proton 9 (recomendado no Linux)"
  echo ""
  echo "2. Launch Options (mínimo):"
  echo "   $LAUNCH_MIN"
  echo ""
  echo "   Laptop NVIDIA híbrido (ex.: Avell):"
  echo "   $LAUNCH_NVIDIA"
  echo ""
  echo "3. Abra o jogo. Se perguntar, aceite o language pack PT-BR."
  echo ""
  echo "   Diálogo Falar (NPC): precisa de internet (Google Translate)."
  echo "=============================================="
}

run_verify() {
  require_game
  export GAME_DIR STEAM_ROOT PROTON_PREFIX UNITY_LINUX STEAM_APP_ID
  export STEAM_APP_IDS PROTON_PREFIXES PROTON_LABELS
  exec "$SCRIPT_DIR/verify-install.sh"
}

main() {
  case "${1:-}" in
    -h|--help)
      usage
      exit 0
      ;;
    --verify)
      run_verify
      ;;
  esac

  info "Project Gorgon PT-BR — instalador"
  info "Origem: $ROOT"
  progress_emit 5 "Iniciando instalação…"
  require_game
  info "Jogo: $GAME_DIR"
  progress_emit 15 "Jogo encontrado"
  require_pack
  progress_emit 22 "Pacote PT-BR verificado"
  install_bepinex
  progress_emit 54 "Ajustando BepInEx (log mínimo)…"
  "$SCRIPT_DIR/apply-bepinex-minimal-logging.sh" "$GAME_DIR"
  install_plugin
  remove_legacy_translator_plugin
  remove_legacy_npctalk_files
  install_translation_pack
  remove_translation_legacy_yaml
  install_game_uninstaller
  install_npctalk_json
  progress_emit 92 "Gerando relatório de instalação…"
  print_finish
  progress_emit 96 "Verificando instalação…"

  export GAME_DIR STEAM_ROOT PROTON_PREFIX UNITY_LINUX STEAM_APP_ID
  export STEAM_APP_IDS PROTON_PREFIXES PROTON_LABELS
  if "$SCRIPT_DIR/verify-install.sh"; then
    progress_emit 100 "Instalação concluída!"
    echo ""
    echo ">>> Pronto! Abra o jogo na Steam."
  else
    progress_emit 100 "Instalação concluída (com avisos)"
    echo ""
    echo "Instalação terminou com avisos — veja acima (geralmente pode abrir o jogo)."
  fi
}

main "$@"
