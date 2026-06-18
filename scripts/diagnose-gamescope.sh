#!/usr/bin/env bash
# Diagnóstico Gamescope + Project Gorgon (Proton) no Linux
set -euo pipefail

echo "=== Ambiente ==="
echo "Sessão: ${XDG_SESSION_TYPE:-?}"
echo "DISPLAY: ${DISPLAY:-?}"
echo "Gamescope: $(gamescope --version 2>&1 | head -1 || echo 'NÃO INSTALADO')"
echo

echo "=== GPU Vulkan (gamescope default) ==="
gamescope -- echo gpu-test 2>&1 | grep -E 'selecting physical|Intel|NVIDIA|Error.*vulkan' || true
echo

echo "=== GPU Vulkan (NVIDIA forçada 10de:25a2) ==="
env __NV_PRIME_RENDER_OFFLOAD=1 __GLX_VENDOR_LIBRARY_NAME=nvidia \
  gamescope --prefer-vk-device 10de:25a2 -- echo gpu-test 2>&1 \
  | grep -E 'selecting physical|Intel|NVIDIA|Error.*vulkan' || true
echo

PLAYER_LOG="$HOME/.steam/debian-installation/steamapps/compatdata/969170/pfx/drive_c/users/steamuser/AppData/LocalLow/Elder Game/Project Gorgon/Player.log"
STEAM_LOG="$HOME/steam-969170.log"

if [[ -f "$PLAYER_LOG" ]]; then
  echo "=== Player.log (última execução) ==="
  tail -20 "$PLAYER_LOG" | grep -E 'Shutting|SteamLogin|Full-Screen|Error|Exception' || tail -5 "$PLAYER_LOG"
  echo
fi

if [[ -f "$STEAM_LOG" ]]; then
  echo "=== steam-969170.log (gamescope swapchain) ==="
  grep -c 'VK_ERROR_OUT_OF_DATE_KHR' "$STEAM_LOG" 2>/dev/null | xargs -I{} echo "VK_ERROR_OUT_OF_DATE_KHR: {} ocorrências"
  grep 'Surface does not allow swapchain' "$STEAM_LOG" 2>/dev/null | tail -3 || echo "(sem erro de surface)"
  echo
fi

echo "=== Launch Options recomendadas (copiar para Steam) ==="
cat <<'EOF'

# Opção 1 — Gamescope + NVIDIA (testar primeiro, SEM -f):
# WINEDLLOVERRIDES ANTES do gamescope — depois de "--" o jogo fecha instantaneamente
env __NV_PRIME_RENDER_OFFLOAD=1 __VK_LAYER_NV_optimus=NVIDIA_only WINEDLLOVERRIDES="winhttp.dll=n,b" gamescope --prefer-vk-device 10de:25a2 -W 1920 -H 1080 -w 1920 -h 1080 -e -- %command%

# Opção 2 — Estável, sem gamescope (barra azul):
WINEDLLOVERRIDES="winhttp.dll=n,b" %command%

EOF
