#!/usr/bin/env bash
# Wrapper: só gera INSTALAR.exe no Windows (PowerShell + PyInstaller)
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
esac

cat <<EOF
INSTALAR.exe só pode ser gerado no Windows.

No Windows (PowerShell), na pasta do projeto:
  powershell -ExecutionPolicy Bypass -File scripts/build-windows-installer.ps1

Depois:
  make pack-windows
  make release-pack-windows

O pacote sem .exe ainda inclui INSTALAR.bat + scripts Python (requer Python instalado).
EOF
exit 1
