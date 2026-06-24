#!/usr/bin/env bash
# Baixa Wine portable + Python Windows para build local do INSTALAR.exe.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CACHE="${ROOT}/.cache"
mkdir -p "${CACHE}/wine-portable" "${CACHE}/win-py"

echo "==> Wine portable…"
if [[ ! -x "${CACHE}/wine-portable/wine-portable.sh" ]]; then
  curl -fsSL -o "${CACHE}/wine-portable/wine-portable.sh" \
    "https://github.com/Kron4ek/wine-portable-executable/releases/download/6.5/wine-portable-6.5-staging-amd64.sh"
  chmod +x "${CACHE}/wine-portable/wine-portable.sh"
fi

echo "==> Python 3.12 Windows (standalone, com tkinter)…"
if [[ ! -f "${CACHE}/win-py/python-standalone/python/python.exe" ]]; then
  curl -fsSL -o "${CACHE}/win-py/python-standalone.tar.gz" \
    "https://github.com/astral-sh/python-build-standalone/releases/download/20260610/cpython-3.12.13%2B20260610-x86_64-pc-windows-msvc-install_only.tar.gz"
  mkdir -p "${CACHE}/win-py/python-standalone"
  tar -xzf "${CACHE}/win-py/python-standalone.tar.gz" -C "${CACHE}/win-py/python-standalone"
fi

echo "OK: ferramentas em ${CACHE}/"
