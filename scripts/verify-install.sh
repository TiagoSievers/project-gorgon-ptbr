#!/usr/bin/env bash
# Verifica se BepInEx, plugin e language pack foram instalados corretamente.
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
[[ -f "$GAME_DIR/BepInEx/core/BepInEx.Unity.IL2CPP.dll" ]] && pass "BepInEx IL2CPP core" || bad "BepInEx core ausente"

plugin="$GAME_DIR/BepInEx/plugins/PgTranslateLive/PgTranslateLive.dll"
[[ -f "$plugin" ]] && pass "Plugin PgTranslateLive.dll" || bad "Plugin ausente em BepInEx/plugins/PgTranslateLive/"

cfg="$GAME_DIR/BepInEx/config/com.pg.translatelive.cfg"
[[ -f "$cfg" ]] && pass "Config com.pg.translatelive.cfg" || note "Config do plugin ausente (criado na 1ª execução?)"

linux_trans="$UNITY_LINUX/Translation/version.json"
proton_ok=0
for i in "${!PROTON_PREFIXES[@]}"; do
  prefix="${PROTON_PREFIXES[$i]}"
  label="${PROTON_LABELS[$i]}"
  proton_trans="$prefix/Translation/version.json"
  if [[ -f "$proton_trans" ]]; then
    pass "Language pack Proton $label ($proton_trans)"
    proton_ok=1
  else
    note "Pack ausente no prefix Proton $label (ok se você não abre essa versão na Steam)"
  fi
done
[[ "$proton_ok" -eq 1 ]] || bad "Pack PT ausente em todos os prefixos Proton"
[[ -f "$linux_trans" ]] && pass "Language pack Linux ($linux_trans)" || note "Pack Linux nativo ausente (ok se só joga via Proton)"

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
