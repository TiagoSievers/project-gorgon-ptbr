---
name: pg-ptbr-update-pack
description: >-
  Atualiza e remonta o pack PT-BR do Project Gorgon (output/Translation,
  dist/PgTranslateLive.dll, pack/ptbr/, releases/*.zip). Dispara ao pedir
  update pack, repack, rebuild pack, gerar zip release, atualizar pack v0.x,
  ou ao abrir esta SKILL.md. Executa o fluxo — não só explica.
---

# Atualizar / repack PT-BR — autoexecução

## Ao carregar (obrigatório)

**Proibido:** resumir a skill, explicar teoria ou pedir "quer que eu comece?" sem executar.

**Obrigatório:** na **primeira resposta**, **AskQuestion** (modo) e **rode** os comandos. Aja, não narre.

**Proibido nesta skill:** editar código do plugin, scripts do repo ou bump de versão — só montar o pack a partir do que já está no repo.

---

## O que o repack produz

| Saída | Origem |
|-------|--------|
| `pack/ptbr/para-Translation/` | `output/Translation/` |
| `pack/ptbr/para-pasta-do-jogo/` | BepInEx vendor + `dist/PgTranslateLive.dll` + `dist/com.pg.translatelive.cfg` |
| `pack/ptbr/SKILL.md`, instaladores, scripts | `scripts/assemble-pack-ptbr.sh` |
| `releases/Project-Gorgon-PT-BR-v*-Pack.zip` | `scripts/release-pack-ptbr.sh` |

Versão do zip vem de `output/Translation/version.json` → campo `Version`.

---

## Fluxo (nesta ordem)

### 1 — Modo

**AskQuestion** imediato:

| Opção | Flag | Quando usar |
|-------|------|-------------|
| Completo (recomendado) | `--full` | Traduziu JSON, mudou plugin, ou não tem certeza |
| Só repack | `--repack-only` | `output/Translation/` e `dist/` já estão atualizados |

### 2 — Raiz do repo

Detectar `REPO_ROOT` (primeiro que existir):

1. Workspace atual (se tiver `Makefile` + `scripts/assemble-pack-ptbr.sh`)
2. Quatro níveis acima deste `SKILL.md` (`.cursor/skills/pg-ptbr-update-pack/` → raiz)

Abortar se não encontrar.

Script da skill:

```bash
UPDATE_SCRIPT=".cursor/skills/pg-ptbr-update-pack/scripts/update-pack.sh"
# ou, no pack extraído futuro: scripts/update-pack.sh (se incluído)
```

### 3 — Dry-run

```bash
bash "$UPDATE_SCRIPT" --full --dry-run
# ou
bash "$UPDATE_SCRIPT" --repack-only --dry-run
```

Mostrar ao usuário **somente** a saída do script (plano de passos + versões). Sem comentário extra.

**AskQuestion:** Continuar com o repack? → **Sim** | **Não**

Se **Não** → parar.

### 4 — Executar

```bash
bash "$UPDATE_SCRIPT" --full
# ou
bash "$UPDATE_SCRIPT" --repack-only
```

**Modo completo** (`--full`) executa, via Makefile:

1. `make write` — gera `output/Translation/*.json`
2. `make build-plugin sync-dist` — compila plugin → `dist/`
3. `make pack-ptbr` — monta `pack/ptbr/`
4. `make release-pack-ptbr` — zip em `releases/`
5. `make verify-pack-ptbr` — extrai zip e valida arquivos obrigatórios

**Modo repack** pula write/build; falha se `output/Translation/version.json` ou `dist/PgTranslateLive.dll` ausentes.

Permissões: build dotnet e zip podem precisar sandbox **all**.

### 5 — Verificar

```bash
REPO="/caminho/translations"
VER="$(python3 -c "import json; print(json.load(open('$REPO/output/Translation/version.json'))['Version'])")"
ZIP="$REPO/releases/Project-Gorgon-PT-BR-v${VER}-Pack.zip"
test -d "$REPO/pack/ptbr/para-Translation" && echo OK pack
test -f "$ZIP" && echo OK zip
test -f "$REPO/pack/ptbr/para-pasta-do-jogo/BepInEx/config/com.pg.translatelive.cfg" && echo OK cfg
```

Reportar só **OK** / **FALHA** por item + caminho do zip.

### 6 — Mensagem final

```
Repack concluído.

  pack/ptbr/
  releases/Project-Gorgon-PT-BR-v{VERSION}-Pack.zip

Para instalar: skill pg-ptbr-install ou Instalador-Linux no zip.
Para desinstalar antes: skill uninstall-pg-ptbr-pack.
```

Substituir `{VERSION}` pelo valor real de `version.json`.

---

## Atalho Makefile (repo dev)

Equivalente ao modo completo:

```bash
make write build-plugin sync-dist pack-ptbr release-pack-ptbr verify-pack-ptbr
```

Equivalente ao repack rápido:

```bash
make pack-ptbr release-pack-ptbr verify-pack-ptbr
```

---

## Pré-requisitos (erros comuns)

| Erro | Causa | Ação |
|------|-------|------|
| `dist/PgTranslateLive.dll ausente` | Plugin não compilado | `--full` ou `make build-plugin sync-dist` |
| `version.json não encontrado` | Pipeline CDN não rodou | `--full` ou `make write` |
| `dotnet não encontrado` | SDK ausente | Instalar .NET 6 SDK |
| `vendor/BepInExPack_IL2CPP.zip ausente` | BepInEx vendor | `assemble-pack-ptbr.sh` baixa automaticamente |
| verify-pack-ptbr FALTA | Pack incompleto | Ler saída do verify; corrigir origem no repo |

---

## Versão do pack

Esta skill **não** altera `output/Translation/version.json`. Para release com número novo, o mantenedor atualiza `Version` (e `Notes` se quiser) **antes** de rodar o repack.

Plugin version no pack vem de `bepinex-plugin/PgTranslateLive/src/main.cs` (`BepInPlugin` attribute) — incluída em `VERSAO.txt` e `LEIA-ME.txt` automaticamente pelo assemble.
