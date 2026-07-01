# deletar/

Artefatos legados movidos da raiz do repo (não usados no pack PT-BR atual).

| Pasta/arquivo | Era |
|---------------|-----|
| `output-pt-BR/` | YAML do pipeline antigo (mod Translator) |
| `scripts/` | `merge-npcs-from-game`, `yaml-to-npctalk-json` |
| `scripts-legado/` | Packs/installers antigos (`assemble-pack*`, `release-pack*`, `build-windows-installer*`, VM/Wine, etc.) |
| `src/` | `yaml_writer.py`, `translator_template_writer.py`, `yaml_io.py`, `translate_daemon.py` |
| `templates-translator-ru/` | Templates RU → YAML pt-BR |
| `copiar/` | Pack legado “copiar e colar” (`assemble-copiar.sh`) |
| `cache/` | Pipeline Python local (`strings.json`, `translations.json`, CDN `v*`) |
| `.cache/` | Downloads/build scratch (BepInEx, Zig, Wine, VM ISO) |
| `dist/` | Instaladores legados (`INSTALAR.exe`, `INSTALAR-COPIAR.exe`, `DESINSTALAR.exe`, `PgPtBr-Installer`, `.pdb`) |
| `installer/` | Instaladores legados (PyInstaller, `INSTALAR` bash, `COMO-INSTALAR*`, `game-uninstall/`, `windows_installer_native.c`) |
| `pack/` | Builds locais antigos: `pg-ptbr/`, `pg-ptbr-windows/` (legado), `ptbr/` (último `make pack-ptbr`) |
| `releases/` | Zips antigos (v0.1.0, v0.1.1, v0.1.2 Copiar/Windows, pastas extraídas) |
| `vendor/Translator/` | Mod PickTeam `Translator.dll` (legado) |

Em `releases/` na raiz ficou só: `Project-Gorgon-PT-BR-v0.1.2-Pack.zip`.

Em `dist/` na raiz ficam só: `PgTranslateLive.dll`, `com.pg.translatelive.cfg`, `BepInEx.logging-minimal.md`.

Em `installer/` na raiz ficou só: `copiar_installer_native.c` (+ este README).

O jogo e o release usam só `output/Translation/*.json` + DLL/cfg em `dist/`.

Se rodar `make fetch` / `make translate` de novo, `cache/` e `.cache/` são recriados na raiz do repo (gitignored).

Pode apagar esta pasta inteira quando confirmar que não precisa mais.
