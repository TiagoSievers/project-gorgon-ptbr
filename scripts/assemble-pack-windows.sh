#!/usr/bin/env bash
# Monta pack/pg-ptbr-windows/ — pacote para jogadores Windows
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PACK_DIR="${PACK_DIR:-$ROOT/pack/pg-ptbr-windows}"
LINUX_PACK="$ROOT/pack/pg-ptbr"

die() { echo "Erro: $*" >&2; exit 1; }

if [[ ! -d "$LINUX_PACK/scripts" ]]; then
  chmod +x "$ROOT/scripts/assemble-pack.sh"
  "$ROOT/scripts/assemble-pack.sh"
fi

echo "Montando $PACK_DIR …"
rm -rf "$PACK_DIR"
mkdir -p "$PACK_DIR/dist" "$PACK_DIR/output" "$PACK_DIR/vendor"

# Conteúdo compartilhado (plugin, tradução, BepInEx)
cp -a "$LINUX_PACK/dist/." "$PACK_DIR/dist/"
cp -a "$LINUX_PACK/output/Translation" "$PACK_DIR/output/"
[[ -d "$LINUX_PACK/output/pt-BR" ]] && cp -a "$LINUX_PACK/output/pt-BR" "$PACK_DIR/output/"
cp "$LINUX_PACK/vendor/BepInExPack_IL2CPP.zip" "$PACK_DIR/vendor/"

cp "$ROOT/installer/COMO-INSTALAR-WINDOWS.txt" "$PACK_DIR/COMO-INSTALAR.txt"
cp "$ROOT/installer/INSTALAR.bat" "$PACK_DIR/INSTALAR.bat"

EXE_SRC="$ROOT/dist/INSTALAR.exe"
if [[ -f "$EXE_SRC" ]]; then
  cp "$EXE_SRC" "$PACK_DIR/INSTALAR.exe"
  echo "Incluído: INSTALAR.exe"
else
  mkdir -p "$PACK_DIR/installer"
  cp "$ROOT/installer/pg_ptbr_installer_windows.py" \
     "$ROOT/installer/windows_core.py" \
     "$PACK_DIR/installer/"
  echo "Aviso: INSTALAR.exe ausente — pacote dev only (INSTALAR.bat requer Python)."
  echo "  Release: powershell -File scripts/build-windows-installer.ps1"
  echo "  ou GitHub Actions → workflow Release"
fi

PACK_VERSION="$(python3 -c "import json; print(json.load(open('$PACK_DIR/output/Translation/version.json'))['Version'])")"

cat > "$PACK_DIR/README-JOGADOR.txt" <<EOF
Project Gorgon — Português (BR) v${PACK_VERSION} — Windows

Leia: COMO-INSTALAR.txt

Resumo: extrair → dois cliques em INSTALAR.exe → abrir o jogo na Steam.
EOF

echo ""
echo "Pacote Windows pronto: $PACK_DIR"
du -sh "$PACK_DIR"
