---
name: pg-ptbr-install
description: >-
  Auto-executa instalação do pack PT-BR do Project Gorgon (copia
  para-pasta-do-jogo/ e para-Translation/). Dispara imediatamente ao abrir,
  mencionar ou anexar SKILL.md, ou pedir instalar pack PT-BR, tradução Project
  Gorgon, copiar diretórios do mod. Não explica a skill — só executa o fluxo.
---

# Instalar pack PT-BR — autoexecução

## Ao carregar (obrigatório)

**Proibido:** resumir a skill, explicar o que ela faz, listar passos futuros ou pedir "quer que eu comece?".

**Obrigatório:** na **primeira resposta**, já chame **AskQuestion** (ou execute se SO/pasta forem óbvios) e rode comandos. Aja, não narre.

---

## Fluxo (execute nesta ordem)

### 1 — SO

**AskQuestion** imediato:

| Opção | Valor |
|-------|-------|
| Linux (Steam/Proton) | `linux` |
| Windows | `windows` |

### 2 — Pasta ptbr/

Detectar `PTBR_ROOT` (primeiro que existir):

1. Diretório onde está este `SKILL.md`
2. `./ptbr` ou `../ptbr` relativo ao workspace
3. `~/Downloads/Project-Gorgon-PT-BR-v*/ptbr`
4. Perguntar caminho só se nenhum funcionar

Validar e abortar com erro curto se falhar:

```bash
PTBR_ROOT="/caminho/ptbr"
test -d "$PTBR_ROOT/para-pasta-do-jogo"
test -f "$PTBR_ROOT/para-Translation/version.json"
test -f "$PTBR_ROOT/para-pasta-do-jogo/BepInEx/plugins/PgTranslateLive/PgTranslateLive.dll"
```

Script de instalação — **mesma lógica que Instalador-Linux** (via `install-pack-files.sh`):

- `$PTBR_ROOT/scripts/install-directories.sh` (pack)
- `.cursor/skills/pg-ptbr-install/scripts/install-directories.sh` (repo)

O core `scripts/install-pack-files.sh` faz: cópia jogo + tradução + **BepInEx log mínimo** + remove Translator legado.

### 3 — Dry-run

Executar **agora** (substituir `--linux`/`--windows` e caminhos):

```bash
bash "$INSTALL_SCRIPT" --linux --dry-run "$PTBR_ROOT"
```

Mostrar ao usuário **somente** a saída do script (plano de cópia). Sem comentário extra.

**AskQuestion:** Continuar com a instalação? → **Sim** | **Não**

Se **Não** → parar.

### 4 — Copiar

```bash
bash "$INSTALL_SCRIPT" --linux --yes "$PTBR_ROOT" "/caminho/Project Gorgon"
```

- `--linux` ou `--windows`
- `GAME_DIR` só se o script não detectar sozinho

### 5 — Verificar

```bash
GAME="/caminho/Project Gorgon"
test -f "$GAME/BepInEx/plugins/PgTranslateLive/PgTranslateLive.dll" && echo OK plugin
test -f "$HOME/.config/unity3d/Elder Game/Project Gorgon/Translation/version.json" && echo OK trans
```

Reportar só OK/FALHA.

### 6 — Launch Options (última mensagem)

**Linux** — única mensagem final (copiar literal):

```
Instalação concluída.

Configure na Steam → Project Gorgon → Propriedades → Opções de inicialização:

  WINEDLLOVERRIDES="winhttp.dll=n,b" %command%

Proton 9 ou Experimental. Log: BepInEx/LogOutput.log
```

**Windows** — só: `Instalação concluída. Abra o jogo pela Steam.`

---

## Caminhos

| Origem | Destino |
|--------|---------|
| `para-pasta-do-jogo/` | `.../steamapps/common/Project Gorgon/` |
| `para-Translation/` | `.../Translation/` |

Linux jogo: `~/.steam/steamapps/common/Project Gorgon` ou `~/.steam/debian-installation/steamapps/common/Project Gorgon`

Windows jogo: `C:\Program Files (x86)\Steam\steamapps\common\Project Gorgon`

Alternativa GUI (só se usuário recusar cópia): `Instalador-Linux` / `Instalador-Windows.exe` em `ptbr/`.
