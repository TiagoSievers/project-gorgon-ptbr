#!/usr/bin/env bash
# Gera dist/INSTALAR.exe no Linux via MinGW-w64 (sem Python/Wine).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ZIG_BIN="$ROOT/.cache/tools/zig-x86_64-linux-0.16.0/zig"
CC_BIN="${CC_BIN:-x86_64-w64-mingw32-gcc}"
SRC="$ROOT/installer/windows_installer_native.c"
OUT="$ROOT/dist/INSTALAR.exe"

die() { echo "Erro: $*" >&2; exit 1; }

[[ -f "$SRC" ]] || die "Fonte não encontrado: $SRC"

mkdir -p "$ROOT/dist"

if command -v "$CC_BIN" >/dev/null 2>&1; then
  echo "==> Compilando INSTALAR.exe (MinGW-w64)…"
  "$CC_BIN" \
    -O2 \
    -municode \
    -mwindows \
    -Wall -Wextra \
    -o "$OUT" \
    "$SRC" \
    -lcomctl32 \
    -lgdi32 \
    -lshell32 \
    -lole32 \
    -ladvapi32
elif [[ -x "$ZIG_BIN" ]]; then
  echo "==> Compilando INSTALAR.exe (Zig portable)…"
  "$ZIG_BIN" cc \
    -target x86_64-windows-gnu \
    -O2 \
    -municode \
    -Wall -Wextra \
    -Wl,--subsystem,windows \
    -o "$OUT" \
    "$SRC" \
    -lcomctl32 \
    -lgdi32 \
    -lshell32 \
    -lole32 \
    -ladvapi32
else
  die "Nenhum compilador Windows encontrado (mingw-w64 ou Zig portable)."
fi

echo ""
echo "OK: $OUT"
ls -lh "$OUT"
