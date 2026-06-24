#!/usr/bin/env bash
# Copia instalador gráfico (zenity) → dist/PgPtBr-Installer
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC="$ROOT/installer/PgPtBr-Installer"
OUT="$ROOT/dist/PgPtBr-Installer"

die() { echo "Erro: $*" >&2; exit 1; }

[[ -f "$SRC" ]] || die "fonte ausente: $SRC"
command -v zenity >/dev/null 2>&1 || echo "Aviso: zenity não instalado nesta máquina (jogador: sudo apt install zenity)"

mkdir -p "$ROOT/dist"
cp "$SRC" "$OUT"
chmod +x "$OUT"
echo "Executável: $OUT"
du -sh "$OUT"

# Opcional: build PyInstaller se python3-tk existir (UI alternativa)
if python3 -c "import tkinter" 2>/dev/null && python3 -m PyInstaller --version >/dev/null 2>&1; then
  echo "python3-tk detectado — para GUI tkinter: python3 -m PyInstaller installer/pg_ptbr_installer.py"
fi
