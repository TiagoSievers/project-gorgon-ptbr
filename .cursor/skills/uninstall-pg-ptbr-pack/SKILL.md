---
name: uninstall-pg-ptbr-pack
description: >-
  Remove arquivos do pack PT-BR Project Gorgon já instalados (BepInEx,
  PgTranslateLive, Translation/). Dispara ao pedir desinstalar, remover mod,
  uninstall pack PT-BR, limpar tradução antes de reinstalar, ou ao abrir
  DESINSTALAR-SKILL.md. Executa o fluxo — não só explica.
---

# Desinstalar pack PT-BR — autoexecução

## Ao carregar (obrigatório)

**Proibido:** resumir a skill, explicar teoria ou pedir "quer que eu comece?" sem executar.

**Obrigatório:** na **primeira resposta**, confirme com **AskQuestion** (se ambíguo) e **rode** os comandos. Aja, não narre.

---

## O que remove

| Local | Conteúdo |
|-------|----------|
| Pasta do jogo | `BepInEx/`, `dotnet/`, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` |
| Proton (342940 + 969170) | `.../Translation/` |
| Linux nativo | `~/.config/unity3d/Elder Game/Project Gorgon/Translation/` |

Legado `BepInEx/plugins/Translator/` — se ainda existir, apagar junto com `BepInEx/`.

**Não** apaga saves nem a pasta do jogo — só o mod PT-BR.

---

## Fluxo (nesta ordem)

### 1 — SO

**AskQuestion** se não for óbvio:

| Opção | Valor |
|-------|-------|
| Linux (Steam/Proton) | `linux` |
| Windows | `windows` |

### 2 — Confirmar

**AskQuestion:** Remover mod PT-BR instalado? → **Sim** | **Não**

Se **Não** → parar.

### 3 — Linux: executar desinstalador

Detectar script (primeiro que existir):

1. `$PTBR_ROOT/scripts/uninstall-directories.sh` (pack extraído)
2. `.cursor/skills/uninstall-pg-ptbr-pack/scripts/uninstall-directories.sh` (repo)
3. `scripts/uninstall.sh` na raiz do repo

```bash
chmod +x "$UNINSTALL_SCRIPT"
bash "$UNINSTALL_SCRIPT" --yes
```

Opcional se auto-detect falhar:

```bash
GAME_DIR="/caminho/Project Gorgon" bash "$UNINSTALL_SCRIPT" --yes
```

Permissões: o script escreve fora do workspace (Steam/Proton) — use sandbox **all** se necessário.

Alternativa GUI (usuário no jogo): executar `uninstall-language-pack-ptbr` na pasta do jogo.

### 4 — Windows

Se pack extraído tiver `game-uninstall/uninstall-language-pack-ptbr.exe`, orientar execução na pasta do jogo.

Senão, remover manualmente (PowerShell como admin se preciso):

- `%USERPROFILE%\AppData\LocalLow\Elder Game\Project Gorgon\Translation\`
- `...\steamapps\common\Project Gorgon\BepInEx\`
- `...\Project Gorgon\dotnet\`, `winhttp.dll`, `doorstop_config.ini`

Ou rodar `installer/install_from_pack.ps1` inverso não existe — use paths acima.

### 5 — Verificar

Linux:

```bash
GAME="$HOME/.steam/debian-installation/steamapps/common/Project Gorgon"
test ! -f "$GAME/BepInEx/plugins/PgTranslateLive/PgTranslateLive.dll" && echo OK plugin removido
test ! -f "$HOME/.config/unity3d/Elder Game/Project Gorgon/Translation/version.json" && echo OK trans linux
```

Reportar só **OK** / **FALHA** por item.

### 6 — Mensagem final

```
Desinstalação concluída. O jogo voltou ao inglês.
Para instalar de novo: use SKILL.md do pack ou make install.
```

---

## Repositório (atalho dev)

```bash
bash scripts/uninstall.sh
# ou
make -C /caminho/translations  # não há target make — use scripts/uninstall.sh
```

---

## Erros comuns

| Sintoma | Ação |
|---------|------|
| Jogo não encontrado | `GAME_DIR=... bash scripts/uninstall.sh` |
| Permission denied | Fechar o jogo; rodar com permissões completas |
| `version.json` ainda existe | Repetir remoção do prefix Proton correto (342940 vs 969170) |
