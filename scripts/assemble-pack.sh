#!/usr/bin/env bash
# Monta pack/pg-ptbr/ — diretório único com TUDO para instalar (jogador ou teste)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PACK_DIR="${PACK_DIR:-$ROOT/pack/pg-ptbr}"
VERSION_FILE="$ROOT/output/Translation/version.json"

die() { echo "Erro: $*" >&2; exit 1; }

[[ -f "$VERSION_FILE" ]] || die "output/Translation/version.json não encontrado"
[[ -f "$ROOT/dist/PgTranslateLive.dll" ]] || die "dist/PgTranslateLive.dll ausente — rode: make sync-dist"

# --- BepInEx em vendor/ (validado) ---
VENDOR_ZIP="$ROOT/vendor/BepInExPack_IL2CPP.zip"
chmod +x "$ROOT/scripts/fetch-bepinex-vendor.sh" "$ROOT/scripts/validate-bepinex-zip.sh" 2>/dev/null || true

ensure_vendor_zip() {
  if [[ -f "$VENDOR_ZIP" ]] && "$ROOT/scripts/validate-bepinex-zip.sh" "$VENDOR_ZIP"; then
    return 0
  fi
  [[ -f "$VENDOR_ZIP" ]] && echo "Aviso: zip corrompido, recriando…" && rm -f "$VENDOR_ZIP"
  if "$ROOT/scripts/fetch-bepinex-vendor.sh" 2>/dev/null \
     && [[ -f "$VENDOR_ZIP" ]] \
     && "$ROOT/scripts/validate-bepinex-zip.sh" "$VENDOR_ZIP"; then
    return 0
  fi
  # Fallback: BepInEx.backup na pasta do jogo
  local game="${GAME_DIR:-$HOME/.steam/debian-installation/steamapps/common/Project Gorgon}"
  if [[ -d "$game/BepInEx.backup" && -f "$game/winhttp.dll.backup" ]]; then
    echo "Criando vendor/ a partir de BepInEx.backup…"
    local staging
    staging="$(mktemp -d)"
    mkdir -p "$staging/BepInExPack"
    cp -a "$game/BepInEx.backup" "$staging/BepInExPack/BepInEx"
    cp "$game/winhttp.dll.backup" "$staging/BepInExPack/winhttp.dll"
    [[ -f "$game/doorstop_config.ini" ]] && cp "$game/doorstop_config.ini" "$staging/BepInExPack/"
    [[ -f "$game/.doorstop_version.backup" ]] && cp "$game/.doorstop_version.backup" "$staging/BepInExPack/.doorstop_version"
    mkdir -p "$(dirname "$VENDOR_ZIP")"
    rm -f "$VENDOR_ZIP"
    (cd "$staging" && zip -qr "$VENDOR_ZIP" BepInExPack)
    rm -rf "$staging"
    "$ROOT/scripts/validate-bepinex-zip.sh" "$VENDOR_ZIP" || die "Falha ao criar vendor/ a partir do backup"
    return 0
  fi
  die "vendor/BepInExPack_IL2CPP.zip inválido — rode: make fetch-bepinex-vendor (com internet)"
}

ensure_vendor_zip

PACK_VERSION="$(python3 -c "import json; print(json.load(open('$VERSION_FILE'))['Version'])")"
CDN_VERSION="$(python3 -c "import json; print(json.load(open('$VERSION_FILE'))['CdnFileVersion'])")"

echo "Montando $PACK_DIR …"
rm -rf "$PACK_DIR"
mkdir -p "$PACK_DIR/scripts" "$PACK_DIR/dist" "$PACK_DIR/output" "$PACK_DIR/vendor"

# Único instalador visível no pacote do jogador
cp "$ROOT/installer/INSTALAR" "$PACK_DIR/INSTALAR"
cp "$ROOT/installer/COMO-INSTALAR.txt" "$PACK_DIR/COMO-INSTALAR.txt"
cp "$ROOT/scripts/install.sh" \
   "$ROOT/scripts/install-paths.sh" \
   "$ROOT/scripts/verify-install.sh" \
   "$PACK_DIR/scripts/"
chmod +x "$PACK_DIR/INSTALAR" \
  "$PACK_DIR/scripts/install.sh" \
  "$PACK_DIR/scripts/verify-install.sh"

# Plugin + tradução + BepInEx
cp -a "$ROOT/dist/PgTranslateLive.dll" "$ROOT/dist/com.pg.translatelive.cfg" "$PACK_DIR/dist/" 2>/dev/null || \
  cp -a "$ROOT/dist/." "$PACK_DIR/dist/"
cp -a "$ROOT/output/Translation" "$PACK_DIR/output/"
[[ -d "$ROOT/output/pt-BR" ]] && cp -a "$ROOT/output/pt-BR" "$PACK_DIR/output/"
cp "$VENDOR_ZIP" "$PACK_DIR/vendor/BepInExPack_IL2CPP.zip"

cat > "$PACK_DIR/README-JOGADOR.txt" <<EOF
Project Gorgon — Português (BR) v${PACK_VERSION}

Leia: COMO-INSTALAR.txt

Resumo: extrair → dois cliques em INSTALAR → configurar Steam → jogar.
EOF

echo ""
echo "Pacote pronto: $PACK_DIR"
du -sh "$PACK_DIR"
echo ""
echo "Testar:  cd $PACK_DIR && ./INSTALAR"
echo "Enviar:  make release-pack  (gera .zip para WinRAR)"
