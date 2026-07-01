#!/usr/bin/env bash
# Verifica se BepInEx, plugin e tradução CDN (Translation/) foram instalados corretamente.
# Uso: ./scripts/verify-install.sh
#      GAME_DIR=... STEAM_ROOT=... ./scripts/verify-install.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# shellcheck source=install-paths.sh
source "$(dirname "${BASH_SOURCE[0]}")/install-paths.sh"

ok=0
fail=0
warn=0

pass() { echo "  OK   $*"; ok=$((ok + 1)); }
bad() { echo "  FALHA $*" >&2; fail=$((fail + 1)); }
note() { echo "  AVISO $*"; warn=$((warn + 1)); }

echo "Project Gorgon PT-BR — verificação de instalação"
echo "Jogo: ${GAME_DIR:-não detectado}"
echo ""

if [[ -z "${GAME_DIR:-}" || ! -d "$GAME_DIR" ]]; then
  bad "Project Gorgon não encontrado (defina GAME_DIR=...)"
  echo ""
  echo "Resumo: $ok ok, $fail falha(s), $warn aviso(s)"
  exit 1
fi

[[ -f "$GAME_DIR/winhttp.dll" ]] && pass "BepInEx loader (winhttp.dll)" || bad "winhttp.dll ausente — rode ./install.sh"
[[ -f "$GAME_DIR/dotnet/coreclr.dll" ]] && pass "BepInEx CoreCLR (dotnet/coreclr.dll)" || bad "dotnet/coreclr.dll ausente — BepInEx IL2CPP incompleto"
[[ -f "$GAME_DIR/BepInEx/core/BepInEx.Unity.IL2CPP.dll" ]] && pass "BepInEx IL2CPP core" || bad "BepInEx core ausente"

plugin="$GAME_DIR/BepInEx/plugins/PgTranslateLive/PgTranslateLive.dll"
[[ -f "$plugin" ]] && pass "Plugin PgTranslateLive.dll" || bad "Plugin ausente em BepInEx/plugins/PgTranslateLive/"

legacy="$GAME_DIR/BepInEx/plugins/Translator"
if [[ -d "$legacy" ]]; then
  bad "Legado duplicado: $legacy — remova (CDN fica só em Translation/)"
else
  pass "Sem mod Translator em plugins/ (sem duplicata)"
fi

cfg="$GAME_DIR/BepInEx/config/com.pg.translatelive.cfg"
[[ -f "$cfg" ]] && pass "Config com.pg.translatelive.cfg" || note "Config do plugin ausente (criado na 1ª execução?)"

[[ -f "$GAME_DIR/uninstall-language-pack-ptbr" || -f "$GAME_DIR/uninstall-language-pack-ptbr.exe" ]] \
  && pass "Desinstalador na pasta do jogo" \
  || note "Desinstalador ausente na pasta do jogo (reinstale com pacote v0.1.0+)"

linux_trans="$CANONICAL_TRANSLATION_DIR/version.json"
if [[ -f "$linux_trans" ]]; then
  pass "Tradução CDN ($CANONICAL_TRANSLATION_DIR/version.json)"
else
  bad "Tradução CDN ausente em $CANONICAL_TRANSLATION_DIR"
fi

if [[ -f "$GAME_DIR/BepInEx/LogOutput.log" ]]; then
  if grep -qi 'error\|exception\|fail' "$GAME_DIR/BepInEx/LogOutput.log" 2>/dev/null; then
    note "LogOutput.log cita error/fail de execuções anteriores — normal se o jogo já abria bem; ignore se tudo funcionar"
  else
    pass "LogOutput.log presente (sem erros óbvios)"
  fi
else
  note "LogOutput.log ainda não existe — normal antes da 1ª execução do jogo (não é falha)"
fi

echo ""
if [[ "$fail" -eq 0 ]]; then
  echo "=============================================="
  echo " Instalação / verificação: SUCESSO"
  echo "=============================================="
  if [[ "$warn" -gt 0 ]]; then
    echo " Aviso(s) acima são informativos — pode abrir o jogo na Steam."
  else
    echo " Tudo certo — pode abrir o jogo na Steam."
  fi
fi
echo ""
echo "Resumo: $ok ok, $fail falha(s), $warn aviso(s)"
[[ "$fail" -eq 0 ]]
