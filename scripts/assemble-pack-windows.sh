#!/usr/bin/env bash
# Monta pack/pg-ptbr-windows/ — pacote para jogadores Windows (sem depender do pack Linux)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PACK_DIR="${PACK_DIR:-$ROOT/pack/pg-ptbr-windows}"
VERSION_FILE="$ROOT/output/Translation/version.json"
VENDOR_ZIP="$ROOT/vendor/BepInExPack_IL2CPP.zip"

die() { echo "Erro: $*" >&2; exit 1; }

[[ -f "$VERSION_FILE" ]] || die "output/Translation/version.json não encontrado"
[[ -f "$ROOT/dist/PgTranslateLive.dll" ]] || die "dist/PgTranslateLive.dll ausente"
[[ -f "$ROOT/dist/Translator.dll" ]] || die "dist/Translator.dll ausente — rode: bash scripts/fetch-translator-vendor.sh"
[[ -d "$ROOT/output/pt-BR" ]] || die "output/pt-BR/ ausente — rode: make write"

chmod +x "$ROOT/scripts/fetch-bepinex-vendor.sh" "$ROOT/scripts/validate-bepinex-zip.sh" \
  "$ROOT/scripts/fetch-translator-vendor.sh" 2>/dev/null || true
if [[ ! -f "$VENDOR_ZIP" ]] || ! "$ROOT/scripts/validate-bepinex-zip.sh" "$VENDOR_ZIP" 2>/dev/null; then
  "$ROOT/scripts/fetch-bepinex-vendor.sh"
fi
[[ -f "$VENDOR_ZIP" ]] || die "vendor/BepInExPack_IL2CPP.zip ausente"

echo "Montando $PACK_DIR …"
rm -rf "$PACK_DIR"
mkdir -p "$PACK_DIR/dist" "$PACK_DIR/output" "$PACK_DIR/vendor"

# Só artefatos do mod — não copiar INSTALAR.exe/.pdb/PgPtBr-Installer de dist/
for artifact in \
  PgTranslateLive.dll \
  Translator.dll \
  com.pg.translatelive.cfg \
  com.pickteam.translator.cfg; do
  [[ -f "$ROOT/dist/$artifact" ]] || die "dist/$artifact ausente"
  cp "$ROOT/dist/$artifact" "$PACK_DIR/dist/"
done
echo "dist/: PgTranslateLive.dll, Translator.dll, configs (sem INSTALAR.exe duplicado)"
cp -a "$ROOT/output/Translation" "$PACK_DIR/output/"
cp -a "$ROOT/output/pt-BR" "$PACK_DIR/output/"
cp "$VENDOR_ZIP" "$PACK_DIR/vendor/BepInExPack_IL2CPP.zip"

cp "$ROOT/installer/COMO-INSTALAR-WINDOWS.txt" "$PACK_DIR/COMO-INSTALAR.txt"
cp -a "$ROOT/installer/game-uninstall" "$PACK_DIR/game-uninstall"

EXE_SRC="$ROOT/dist/INSTALAR.exe"
[[ -f "$EXE_SRC" ]] || die \
  "INSTALAR.exe ausente em dist/ — gere: powershell -File scripts/build-windows-installer.ps1"
cp "$EXE_SRC" "$PACK_DIR/INSTALAR.exe"
echo "Incluído: INSTALAR.exe"

PACK_VERSION="$(python3 -c "import json; print(json.load(open('$VERSION_FILE'))['Version'])")"

cat > "$PACK_DIR/README-JOGADOR.txt" <<EOF
Project Gorgon — Português (BR) v${PACK_VERSION} — Windows

Leia: COMO-INSTALAR.txt

Resumo: extrair → dois cliques em INSTALAR.exe → abrir o jogo na Steam.
Para desinstalar depois: uninstall-language-pack-ptbr.exe na pasta do jogo.
EOF

echo ""
echo "Pacote Windows pronto: $PACK_DIR"
du -sh "$PACK_DIR"
