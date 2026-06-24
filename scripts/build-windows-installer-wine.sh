#!/usr/bin/env bash
# Gera dist/INSTALAR.exe no Linux via Wine + Python Windows (standalone).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CACHE="${ROOT}/.cache"
WINE="${CACHE}/wine-portable/wine-portable.sh"
PY="${CACHE}/win-py/python-standalone/python/python.exe"
GET_PIP="${CACHE}/get-pip.py"
export WINEPREFIX="${CACHE}/wine-prefix"
export WINEARCH=win64
export WINEDEBUG=-all

die() { echo "Erro: $*" >&2; exit 1; }

[[ -x "$WINE" ]] || die "Wine portable ausente — rode scripts/setup-wine-build.sh"
[[ -f "$PY" ]] || die "Python Windows ausente — rode scripts/setup-wine-build.sh"

mkdir -p "$CACHE" "$ROOT/dist"

if [[ ! -f "$GET_PIP" ]]; then
  curl -fsSL -o "$GET_PIP" https://bootstrap.pypa.io/get-pip.py
fi

echo "==> pip + PyInstaller (Wine)…"
"$WINE" "$PY" "$GET_PIP" --quiet 2>/dev/null || "$WINE" "$PY" "$GET_PIP"
"$WINE" "$PY" -m pip install --upgrade pip pyinstaller --quiet

echo "==> Testando tkinter…"
"$WINE" "$PY" -c "import tkinter; print('tkinter ok')"

echo "==> PyInstaller…"
WIN_ROOT="$("$WINE" winepath -w "$ROOT")"
WIN_INSTALLER="$("$WINE" winepath -w "$ROOT/installer")"
"$WINE" cmd /c "cd /d \"$WIN_INSTALLER\" && \"$("$WINE" winepath -w "$PY")\" -m PyInstaller --clean --noconfirm INSTALAR-windows.spec"

BUILT="$ROOT/installer/dist/INSTALAR.exe"
[[ -f "$BUILT" ]] || die "PyInstaller não gerou $BUILT"

cp -f "$BUILT" "$ROOT/dist/INSTALAR.exe"
echo ""
echo "OK: $ROOT/dist/INSTALAR.exe"
ls -lh "$ROOT/dist/INSTALAR.exe"
