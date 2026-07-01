# Project Gorgon — PT-BR

Tradução comunitária (pt-BR) para [Project Gorgon](https://store.steampowered.com/app/342940/Project_Gorgon/): **language pack CDN** (`Translation/strings_*.json`) + plugin **PgTranslateLive** (diálogo **Falar** com NPC via Google).

Projeto fan — não oficial. Requer o jogo na Steam.

Repositório: **https://github.com/TiagoSievers/project-gorgon-ptbr**

---

## Arquitetura in-game

| Camada | Onde | O que traduz |
|--------|------|--------------|
| **Language pack** | `…/LocalLow/Elder Game/Project Gorgon/Translation/` (Proton app **342940**) | UI, itens, quests (`strings_*.json`) |
| **PgTranslateLive** | `BepInEx/plugins/PgTranslateLive/` | Falar ao vivo (`strings_npctalk.json` + Google) |

**Não** use `BepInEx/plugins/Translator/` — mod legado removido.

---

## Jogador — instalar

1. Baixe **`Project-Gorgon-PT-BR-v*-Pack.zip`** em [Releases](https://github.com/TiagoSievers/project-gorgon-ptbr/releases/latest)
2. Extraia → pasta **`ptbr/`**
3. **Linux:** dois cliques em **`Instalador-Linux`** (requer `zenity`: `sudo apt install zenity`)
4. **Windows:** dois cliques em **`Instalador-Windows.exe`**
5. Abra o jogo pela Steam

Leia **`LEIA-ME.txt`** dentro do zip. Para guia com qualquer IA: **[IA_instruction.md](IA_instruction.md)**. Com Cursor, abra **`SKILL.md`** do pack para instalação guiada.

### Steam — Launch Options (Linux + Proton)

```
WINEDLLOVERRIDES="winhttp.dll=n,b" %command%
```

Compatibilidade: **Proton 9** (ou Experimental). Windows não precisa disso.

### Desinstalar

- **`DESINSTALAR-SKILL.md`** no pack (Cursor), ou
- `scripts/uninstall.sh` na pasta do pack

---

## Desenvolvedor — clone rápido

```bash
git clone https://github.com/TiagoSievers/project-gorgon-ptbr.git
cd project-gorgon-ptbr
make install-plugin    # compila + instala plugin (exige BepInEx no jogo)
# ou instalação completa:
make install
```

Versionado no repo: `output/Translation/`, `dist/PgTranslateLive.dll`, `dist/com.pg.translatelive.cfg`.

---

## Mantenedor — pipeline e release

```bash
make pipeline              # fetch → extract → translate → write → output/Translation/
make sync-dist             # DLL compilada → dist/
make pack-ptbr             # monta pack/ptbr/
make release-pack-ptbr     # → releases/Project-Gorgon-PT-BR-v*-Pack.zip
make verify-pack-ptbr      # valida o zip
```

Edite **`glossary.json`** para termos fixos no Google Translate. Comandos: **`make help`**.

Plugin: **`make build-plugin`**, **`make install-plugin`**, logs: **`make enable-plugin-logging`**.

Fluxo interno do plugin: [`bepinex-plugin/PgTranslateLive/FLOW.md`](bepinex-plugin/PgTranslateLive/FLOW.md)

---

## Estrutura do repositório

```
├── output/Translation/     # language pack (versionado)
├── dist/                   # PgTranslateLive.dll + cfg (versionado)
├── bepinex-plugin/         # código-fonte C#
├── scripts/                # pack-ptbr, install, BepInEx
├── src/                    # pipeline Python (CDN)
├── installer/              # copiar_installer_native.c (Windows GUI)
├── glossary.json           # termos fixos EN→PT
├── pack/                   # gitignored — make pack-ptbr
├── releases/               # gitignored — zip local (+ README)
├── vendor/                 # BepInEx zip local (gitignored)
└── deletar/                # legado arquivado (limpar antes do push se quiser)
```

---

## Créditos

- Dados do jogo: **Elder Game, LLC**
- Tradução automática (Google) — revisão humana recomendada
- BepInEx: [Thunderstore IL2CPP](https://thunderstore.io/c/project-gorgon/p/BepInEx/BepInExPack_IL2CPP/)
