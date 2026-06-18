# Project Gorgon — PT-BR

Tradução em português brasileiro para [Project Gorgon](https://store.steampowered.com/app/342940/Project_Gorgon/) (versão paga ou demo na Steam): **language pack** (UI, itens, quests) + plugin **PgTranslateLive** (diálogo **Falar** com NPC via Google).

Projeto fan — não oficial. Requer o jogo na Steam (pago ou demo).

---

## Baixar (GitHub Release)

1. Abra **[Releases](https://github.com/TiagoSievers/project-gorgon-ptbr/releases)** no GitHub  
2. Clique na release mais recente (ex.: **v0.2.0**)  
3. Role até **Assets**  
4. Baixe **só o arquivo do seu sistema** (ignore *Source code* — é código-fonte automático do GitHub):

| Se você joga em… | Arquivo para clicar |
|------------------|---------------------|
| **Linux / Steam Deck / Proton** | `Project-Gorgon-PT-BR-v…-**Linux**.zip` |
| **Windows** | `Project-Gorgon-PT-BR-v…-**Windows**.zip` |

Link direto da última release:  
**https://github.com/TiagoSievers/project-gorgon-ptbr/releases/latest**

---

## Jogar — Linux (Steam + Proton)

1. Baixe **`Project-Gorgon-PT-BR-v…-Linux.zip`** (tabela acima)
2. Extraia — aparece a pasta **`pg-ptbr/`**
3. **Dois cliques** em **`INSTALAR`** (leia `COMO-INSTALAR.txt` se precisar)

   No Ubuntu/Nautilus, na 1ª vez: botão direito → Propriedades → marque **Permitir executar como programa**.

   Requer `zenity` (Ubuntu: `sudo apt install zenity`).

   Alternativa no terminal: `./install.sh`

4. Abra o jogo na Steam e configure **Launch Options** (o instalador mostra o texto ao final).

**Mínimo (Proton + BepInEx):**

```
WINEDLLOVERRIDES="winhttp.dll=n,b" %command%
```

**Linux — laptop NVIDIA híbrido (ex.: Avell):**

```
env __NV_PRIME_RENDER_OFFLOAD=1 DXVK_FILTER_DEVICE_NAME="RTX 3050" WINEDLLOVERRIDES="winhttp.dll=n,b" %command%
```

Também: Propriedades → Compatibilidade → **Forçar Steam Play** → Proton 9.

**O instalador faz tudo:** BepInEx (já vem no pacote Release), plugin, language pack PT em `Translation/`. O jogador **não** precisa instalar Python nem .NET. Internet só para **Falar** com NPC (Google).

---

## Jogar — Windows

1. Baixe **`Project-Gorgon-PT-BR-v…-Windows.zip`** (tabela em [Baixar](#baixar-github-release))
2. Extraia a pasta **`pg-ptbr-windows/`**
3. **Dois cliques em `INSTALAR.exe`** — não precisa instalar Python

   (`INSTALAR.bat` só é fallback de desenvolvimento se o `.exe` não existir.)

Não precisa de Launch Options do Proton (só Linux).

**Mantenedor — gerar zips para Release (com INSTALAR.exe no Windows):**

Dispare no GitHub: **Actions → Release → Run workflow**  
Ou crie tag `v0.2.0` — publica Linux + Windows automaticamente.

Local (Windows VM, com PowerShell):

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-windows-installer.ps1
```

Depois no Linux (ou Git Bash):

```bash
make release-pack-windows   # exige dist/INSTALAR.exe
make release-pack           # Linux
```

---

## Desenvolver / clonar

```bash
git clone https://github.com/TiagoSievers/project-gorgon-ptbr.git
cd project-gorgon-ptbr
make install
# ou: ./install.sh
```

O repositório inclui `output/Translation/` (PT pronto) e `dist/PgTranslateLive.dll` (plugin pré-compilado).

---

## Atualizar tradução (mantenedor)

Quando o CDN do jogo mudar (nova versão):

```bash
make pipeline          # fetch → extract → translate (Google) → write
make sync-dist         # recompila e copia DLL → dist/
make pack              # monta pack/pg-ptbr/ (pasta completa para testar)
make release-pack      # → Project-Gorgon-PT-BR-v*-Linux.zip
make release-pack-windows  # → Project-Gorgon-PT-BR-v*-Windows.zip
```

Edite `glossary.json` para termos fixos. Detalhes do pipeline: `make help`.

---

## O que o plugin faz

| Recurso | Como |
|---------|------|
| Menu, itens, quests, UI | `output/Translation/` — language pack do jogo |
| Diálogo **Falar** (NPC) | Plugin + Google Translate ao vivo |
| Log do plugin | `Project Gorgon/BepInEx/LogOutput.log` |
| Config | `BepInEx/config/com.pg.translatelive.cfg` |

Fluxo interno do plugin: [`bepinex-plugin/PgTranslateLive/FLOW.md`](bepinex-plugin/PgTranslateLive/FLOW.md)

---

## Pacote de instalação (`pack/pg-ptbr/`)

Tudo que o jogador precisa fica **numa pasta só**:

```
pack/pg-ptbr/
├── INSTALAR               # único instalador (dois cliques)
├── COMO-INSTALAR.txt
├── dist/                  # interno
├── output/                # interno
├── vendor/                # interno
└── scripts/               # interno
├── install.sh
├── dist/                  # plugin
├── output/Translation/    # language pack PT
├── vendor/                # BepInEx (offline)
├── scripts/
└── README-JOGADOR.txt
```

**Não** copie só `installer/` — use `pack/pg-ptbr/` inteira ou o `.zip` **Linux** da Release.

## Estrutura do repositório (desenvolvimento)

```
├── pack/pg-ptbr/           # gerado: make pack (pacote completo — distribuir/testar)
├── installer/
│   └── PgPtBr-Installer      # fonte do instalador gráfico (copiado → pack/)
├── scripts/
│   ├── install.sh          # instalador completo
│   ├── install-paths.sh    # detecção Steam / jogo
│   ├── verify-install.sh   # checagem pós-instalação
│   ├── build-installer.sh  # gera dist/PgPtBr-Installer
│   └── release-pack.sh     # monta tarball da Release
├── dist/
│   ├── PgTranslateLive.dll
│   ├── com.pg.translatelive.cfg
│   └── PgPtBr-Installer      # na Release: dois cliques
├── vendor/
│   └── BepInExPack_IL2CPP.zip  # BepInEx incluso na Release (make fetch-bepinex-vendor)
├── output/
│   ├── Translation/        # language pack PT (versionado)
│   └── pt-BR/              # YAML mod Translator (opcional)
├── bepinex-plugin/         # código-fonte do plugin
├── src/                    # pipeline Python (mantenedor)
├── cache/                  # gitignored — pipeline
└── releases/               # gitignored — tarballs gerados
```

---

## Comandos úteis

```bash
make install          # instalar no jogo
make verify-install   # checar BepInEx + plugin + pack
make release-pack     # pacote para GitHub Release
make pipeline         # regerar tradução (mantenedor)
make paths            # caminhos Steam detectados
./scripts/diagnose-gamescope.sh   # diagnóstico vídeo Linux
```

---

## Créditos e aviso

- Dados do jogo: copyright **Elder Game, LLC**
- Tradução automática (Google) — revisão humana recomendada
- BepInEx: [Thunderstore](https://thunderstore.io/c/project-gorgon/p/BepInEx/BepInExPack_IL2CPP/)
