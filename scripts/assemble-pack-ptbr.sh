#!/usr/bin/env bash
# Monta pack/ptbr/ — pacote completo PT-BR (INSTALAR + pastas prontas).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT_DIR="${OUT_DIR:-$ROOT/pack/ptbr}"
VERSION_FILE="$ROOT/output/Translation/version.json"
VENDOR_ZIP="$ROOT/vendor/BepInExPack_IL2CPP.zip"
GAME_DIR_NAME="para-pasta-do-jogo"
TRANS_DIR_NAME="para-Translation"

die() { echo "Erro: $*" >&2; exit 1; }

[[ -f "$VERSION_FILE" ]] || die "output/Translation/version.json não encontrado"
[[ -f "$ROOT/dist/PgTranslateLive.dll" ]] || die "dist/PgTranslateLive.dll ausente — rode: make build-plugin sync-dist"

chmod +x "$ROOT/scripts/validate-bepinex-zip.sh" 2>/dev/null || true
if [[ ! -f "$VENDOR_ZIP" ]] || ! "$ROOT/scripts/validate-bepinex-zip.sh" "$VENDOR_ZIP" 2>/dev/null; then
  chmod +x "$ROOT/scripts/fetch-bepinex-vendor.sh"
  "$ROOT/scripts/fetch-bepinex-vendor.sh"
fi
[[ -f "$VENDOR_ZIP" ]] || die "vendor/BepInExPack_IL2CPP.zip ausente"

PACK_VERSION="$(python3 -c "import json; print(json.load(open('$VERSION_FILE'))['Version'])")"
PLUGIN_VERSION="$(grep -oP '(?<=\[BepInPlugin\("com.pg.translatelive", "Pg Translate Live", ")[^"]+' \
  "$ROOT/bepinex-plugin/PgTranslateLive/src/main.cs" 2>/dev/null || echo "?")"

echo "Montando $OUT_DIR …"
rm -rf "$OUT_DIR"
mkdir -p "$OUT_DIR/$GAME_DIR_NAME" "$OUT_DIR/$TRANS_DIR_NAME"

staging="$(mktemp -d)"
trap 'rm -rf "$staging"' EXIT
unzip -q -o "$VENDOR_ZIP" -d "$staging"

if [[ -d "$staging/BepInExPack" ]]; then
  cp -a "$staging/BepInExPack/." "$OUT_DIR/$GAME_DIR_NAME/"
else
  cp -a "$staging/." "$OUT_DIR/$GAME_DIR_NAME/"
fi

mkdir -p "$OUT_DIR/$GAME_DIR_NAME/BepInEx/plugins/PgTranslateLive"
mkdir -p "$OUT_DIR/$GAME_DIR_NAME/BepInEx/config"
cp "$ROOT/dist/PgTranslateLive.dll" "$OUT_DIR/$GAME_DIR_NAME/BepInEx/plugins/PgTranslateLive/"
cp "$ROOT/dist/com.pg.translatelive.cfg" "$OUT_DIR/$GAME_DIR_NAME/BepInEx/config/"

"$ROOT/scripts/apply-bepinex-minimal-logging.sh" "$OUT_DIR/$GAME_DIR_NAME"

cp -a "$ROOT/output/Translation/." "$OUT_DIR/$TRANS_DIR_NAME/"

cat > "$OUT_DIR/LEIA-ME.txt" <<EOF
Project Gorgon — PT-BR v${PACK_VERSION}
Plugin de diálogos: PgTranslateLive v${PLUGIN_VERSION}

══════════════════════════════════════════════════════════════
JEITO FÁCIL (recomendado)
══════════════════════════════════════════════════════════════

1. Extraia o zip inteiro numa pasta (ex.: Downloads).
2. Entre na pasta ptbr/.
3. Linux:   dois cliques em Instalador-Linux
   Windows: dois cliques em Instalador-Windows.exe
4. Siga as janelas na tela (Continuar → aguarde → Fechar).
5. Abra o jogo pela Steam.

Pronto. O instalador copia tudo sozinho.

Linux: se o instalador não abrir, instale o zenity:
  sudo apt install zenity

══════════════════════════════════════════════════════════════
PRECISA DE AJUDA? (use com qualquer IA)
══════════════════════════════════════════════════════════════

Abra o arquivo IA_instruction.md (no repositório ou dentro do zip em ptbr/),
copie TODO o texto e cole em um chat de IA (ChatGPT, Claude, Gemini, Copilot, etc.).
A IA vai te guiar passo a passo conforme seu sistema.

Usa Cursor? Abra ou mencione SKILL.md — a instalação começa
automaticamente (pergunta Linux/Windows, copia, Launch Options).

Para desinstalar antes de reinstalar: DESINSTALAR-SKILL.md (Cursor)
ou execute uninstall-language-pack-ptbr na pasta do jogo.

══════════════════════════════════════════════════════════════
JEITO MANUAL (copiar e colar você mesmo)
══════════════════════════════════════════════════════════════

Depois de extrair o zip, você verá duas pastas importantes
dentro de ptbr/:

  ${GAME_DIR_NAME}/     → vai para a pasta do jogo na Steam
  ${TRANS_DIR_NAME}/    → vai para a pasta Translation/

──────────────────────────────────────────────────────────────
Passo 1 — Pasta do jogo
──────────────────────────────────────────────────────────────

Onde fica o Project Gorgon na Steam:

  Linux:
    ~/.steam/steamapps/common/Project Gorgon
    (caminho pode variar conforme sua instalação Steam)

  Windows:
    C:\\Program Files (x86)\\Steam\\steamapps\\common\\Project Gorgon

O que fazer:
  Abra a pasta ${GAME_DIR_NAME}/, selecione TUDO que está dentro
  e cole dentro da pasta do Project Gorgon na Steam.
  Se perguntar para substituir arquivos, pode aceitar.

──────────────────────────────────────────────────────────────
Passo 2 — Tradução (menus, itens, quests)
──────────────────────────────────────────────────────────────

Onde fica a pasta Translation:

  Windows:
    %USERPROFILE%\\AppData\\LocalLow\\Elder Game\\Project Gorgon\\Translation\\

  Linux (jogo pelo Proton — versão paga):
    ~/.steam/steamapps/compatdata/342940/pfx/drive_c/users/steamuser/AppData/LocalLow/Elder Game/Project Gorgon/Translation/

  Linux (alternativa):
    ~/.config/unity3d/Elder Game/Project Gorgon/Translation/

O que fazer:
  Abra a pasta ${TRANS_DIR_NAME}/, selecione TUDO e cole dentro
  da pasta Translation (crie a pasta se não existir).
  No Linux com Proton, copie para mais de um caminho acima
  se não tiver certeza qual o jogo usa.

──────────────────────────────────────────────────────────────
Passo 3 — Steam (só Linux + Proton, uma vez)
──────────────────────────────────────────────────────────────

Steam → botão direito no jogo → Propriedades →
Opções de inicialização → cole:

  WINEDLLOVERRIDES="winhttp.dll=n,b" %command%

Compatibilidade: Proton 9 (ou Experimental).

Windows: não precisa disso.

══════════════════════════════════════════════════════════════
Como saber se deu certo
══════════════════════════════════════════════════════════════

Abra o jogo. Para conferir:

  • Existe: .../Project Gorgon/BepInEx/plugins/PgTranslateLive/PgTranslateLive.dll
  • Existe: .../Translation/version.json
  • No log pode aparecer: Pg Translate Live v${PLUGIN_VERSION}
    (arquivo: .../Project Gorgon/BepInEx/LogOutput.log)

Projeto de fã — não oficial. Elder Game, LLC.
EOF

[[ -f "$ROOT/IA_instruction.md" ]] || die "IA_instruction.md ausente na raiz do repo"
cp "$ROOT/IA_instruction.md" "$OUT_DIR/IA_instruction.md"
sed -i "s/PACK_VERSION_PLACEHOLDER/${PACK_VERSION}/g; s/PLUGIN_VERSION_PLACEHOLDER/${PLUGIN_VERSION}/g" \
  "$OUT_DIR/IA_instruction.md"

SKILL_SRC="$ROOT/.cursor/skills/pg-ptbr-install/SKILL.md"
INSTALL_SCRIPT_SRC="$ROOT/.cursor/skills/pg-ptbr-install/scripts/install-directories.sh"
[[ -f "$SKILL_SRC" ]] || die "SKILL.md ausente: $SKILL_SRC"
[[ -f "$INSTALL_SCRIPT_SRC" ]] || die "install-directories.sh ausente: $INSTALL_SCRIPT_SRC"

mkdir -p "$OUT_DIR/scripts"
cp "$SKILL_SRC" "$OUT_DIR/SKILL.md"
cp "$INSTALL_SCRIPT_SRC" "$OUT_DIR/scripts/install-directories.sh"
cp "$ROOT/scripts/install-pack-files.sh" "$OUT_DIR/scripts/"
cp "$ROOT/scripts/apply-bepinex-minimal-logging.sh" "$OUT_DIR/scripts/"
chmod +x "$OUT_DIR/scripts/install-directories.sh" \
  "$OUT_DIR/scripts/install-pack-files.sh" \
  "$OUT_DIR/scripts/apply-bepinex-minimal-logging.sh"
sed -i 's|\.cursor/skills/pg-ptbr-install/scripts/install-directories\.sh|scripts/install-directories.sh|g' \
  "$OUT_DIR/SKILL.md"
echo "Incluído: SKILL.md + scripts/install-directories.sh"

UNINSTALL_SKILL_SRC="$ROOT/.cursor/skills/uninstall-pg-ptbr-pack/SKILL.md"
UNINSTALL_SCRIPT_SRC="$ROOT/.cursor/skills/uninstall-pg-ptbr-pack/scripts/uninstall-directories.sh"
[[ -f "$UNINSTALL_SKILL_SRC" ]] || die "DESINSTALAR-SKILL ausente: $UNINSTALL_SKILL_SRC"
[[ -f "$UNINSTALL_SCRIPT_SRC" ]] || die "uninstall-directories.sh ausente: $UNINSTALL_SCRIPT_SRC"

cp "$UNINSTALL_SKILL_SRC" "$OUT_DIR/DESINSTALAR-SKILL.md"
cp "$UNINSTALL_SCRIPT_SRC" "$OUT_DIR/scripts/uninstall-directories.sh"
cp "$ROOT/scripts/uninstall.sh" "$OUT_DIR/scripts/"
cp "$ROOT/scripts/install-paths.sh" "$OUT_DIR/scripts/"
chmod +x "$OUT_DIR/scripts/uninstall-directories.sh" \
  "$OUT_DIR/scripts/uninstall.sh"
sed -i 's|\.cursor/skills/uninstall-pg-ptbr-pack/scripts/uninstall-directories\.sh|scripts/uninstall-directories.sh|g' \
  "$OUT_DIR/DESINSTALAR-SKILL.md"
echo "Incluído: DESINSTALAR-SKILL.md + scripts/uninstall-directories.sh"

cat > "$OUT_DIR/VERSAO.txt" <<EOF
pack=${PACK_VERSION}
plugin=${PLUGIN_VERSION}
gerado=$(date -Iseconds)
EOF

chmod +x "$ROOT/scripts/build-copiar-installer-native.sh" "$ROOT/scripts/instalador-linux.sh"
if "$ROOT/scripts/build-copiar-installer-native.sh"; then
  cp "$ROOT/dist/INSTALAR-COPIAR.exe" "$OUT_DIR/Instalador-Windows.exe"
  echo "Incluído: Instalador-Windows.exe"
else
  echo "Aviso: Instalador-Windows.exe não compilado — use Instalador-Linux ou LEIA-ME.txt"
fi
cp "$ROOT/scripts/instalador-linux.sh" "$OUT_DIR/Instalador-Linux"
chmod +x "$OUT_DIR/Instalador-Linux"
echo "Incluído: Instalador-Linux + Instalador-Windows.exe"

echo ""
echo "Pronto: $OUT_DIR"
du -sh "$OUT_DIR"
echo ""
echo "  ${OUT_DIR}/${GAME_DIR_NAME}/     → pasta do jogo"
echo "  ${OUT_DIR}/${TRANS_DIR_NAME}/    → pasta(s) Translation/"
echo "  ${OUT_DIR}/LEIA-ME.txt"
echo "  ${OUT_DIR}/IA_instruction.md"
echo "  ${OUT_DIR}/SKILL.md"
echo "  ${OUT_DIR}/DESINSTALAR-SKILL.md"
