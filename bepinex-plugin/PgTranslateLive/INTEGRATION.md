# Arquitetura de tradução (CDN JSON + PgTranslateLive)

> **Runtime:** somente `Translation/*.json` (language pack CDN) + plugin **PgTranslateLive**.
> Não instale YAML nem `Translator.dll` — legado removido.

## O jogo lê

Pasta `Translation/` (AppData / Proton):

| Arquivo | Conteúdo |
|---------|----------|
| `strings_ui.json` | UI (login, menus) — chaves CDN `ui.*` + entradas legado EN→PT |
| `strings_npctalk.json` | Falas **Falar** (ShowTalk) — texto EN exato → PT. **Não** subtexto de NPC (`strings_npcs.json`) |
| `strings_npcs.json`, `strings_items.json`, … | Fallback via `CdnStringStore` |

## Plugin PgTranslateLive

| Módulo | Função |
|--------|--------|
| **UiTextPatch** | Hook `UITools.SetText` / `TMP_Text` — UI estática via `CdnStringStore` (`LabelsEnabled`) |
| **NpcTalkStore** | Falas Falar via `strings_npctalk.json` |
| **CdnStringStore** | Cruza EN oficial (CDN cache) com PT do pack |
| **TranslateClient** | Google Translate para falas ainda não cacheadas |

## Pipeline dev (repo)

`make write` gera `output/Translation/*.json` para o pack (versionado no repo).
