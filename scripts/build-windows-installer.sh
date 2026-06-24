#!/usr/bin/env bash
# Wrapper: gera INSTALAR.exe no Windows (PyInstaller) ou Linux (MinGW-w64).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

case "$(uname -s 2>/dev/null)" in
  MINGW*|MSYS*|CYGWIN*|Windows_NT*)
    if command -v powershell.exe >/dev/null 2>&1; then
      powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$ROOT/scripts/build-windows-installer.ps1"
      exit $?
    fi
    if command -v pwsh >/dev/null 2>&1; then
      pwsh -NoProfile -ExecutionPolicy Bypass -File "$ROOT/scripts/build-windows-installer.ps1"
      exit $?
    fi
    ;;
  Linux*)
    if command -v x86_64-w64-mingw32-gcc >/dev/null 2>&1; then
      "$ROOT/scripts/build-windows-installer-native.sh"
      exit $?
    fi
    ;;
esac

cat <<EOF
Não foi possível gerar INSTALAR.exe neste ambiente.

Opções suportadas:
  1. Windows + PowerShell:
     powershell -ExecutionPolicy Bypass -File scripts/build-windows-installer.ps1

  2. Linux + MinGW-w64:
     sudo apt-get install -y mingw-w64
     bash scripts/build-windows-installer-native.sh

  3. Linux + Zig portable no projeto:
     bash scripts/build-windows-installer-native.sh

Depois:
  make pack-windows
  make release-pack-windows
EOF
exit 1
