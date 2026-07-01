# BACKUP v0.14.30 — Pg Translate Live

**Data:** 2026-06-29  
**Status:** versão de referência validada com log de subtexto por camadas (DebugSubtexto)  
**Plugin ID:** `com.pg.translatelive`  
**Assembly:** `PgTranslateLive.dll`

---

## Snapshot do código

| Item | Valor |
|------|-------|
| Fonte ativa | `src/main.cs` (único `.cs` do plugin) |
| Linhas | ~6757 |
| SHA-256 `main.cs` | `b5e8c27c75da09f3a802ae9ab6378fd6bc539ec6dbaee84f0e97ff76996c3a55` |
| Cópia congelada | `backup/v0.14.30/main.cs` |
| DLL Release | `bin/Release/net6.0/PgTranslateLive.dll` |

### Restaurar esta versão

```bash
cp bepinex-plugin/PgTranslateLive/backup/v0.14.30/main.cs \
   bepinex-plugin/PgTranslateLive/src/main.cs
make build-plugin install-plugin
```

Reinicie o jogo e confirme no log: `Pg Translate Live v0.14.30`.

---

## Arquitetura do código

Todo o C# está unificado em **`src/main.cs`**, organizado por `#region`:

| Região | Responsabilidade |
|--------|------------------|
| **Config** | `PluginSettings`, `PatchStrategy` |
| **Paths** | `GameDataPaths` — pasta `Translation/` do jogo |
| **Logging** | `LogAscii`, `TraceLog`, `TalkLog` |
| **Il2Cpp** | `Il2CppStringHelper`, `Il2CppArrayReflection` |
| **Stores** | `CdnStringStore`, `NpcTalkStore`, `NpcRegistryStore` |
| **Translate** | `TranslateClient`, `GoogleTranslate`, `GoogleAsyncQueue` |
| **Npc** | `NpcNameResolver`, `EntityNpcIdCache`, `NpcIdPatterns` |
| **Talk** | patches, sessão, diálogo, subtexto, discovery |
| **Interaction** | `InteractionClickPatch`, `UiInteractionFinder` |
| **Ui** | `UiTextPatch` (desligado v0.13.5) |
| **Debug** | probes, `TalkDiagnostics` |
| **Plugin** | entrada BepInEx (`Load()`) |

---

## Regras de hooks (Harmony)

### Estratégia padrão: `DynamicShowTalk`

Config: `[Hooks] PatchStrategy = DynamicShowTalk`

| Regra | Comportamento |
|-------|---------------|
| **ShowTalk** | patch aplicado **só durante diálogo** (`ProcessPreTalkScreen` → `ProcessEndInteraction`) |
| **DisplayTalkScreen** | patch **permanente** (pinta texto na UI) |
| **Mapa aberto** | ShowTalk **não** patchado → evita travada v0.11 |
| **Fallback** | se `ProcessPreTalkScreen`/`ProcessEndInteraction` não existirem na classe indexada → `AlwaysShowTalk` |

Outras estratégias (só para teste):

| Estratégia | Uso |
|------------|-----|
| `ProcessTalkScreen` | hook alternativo no pipeline de diálogo |
| `AlwaysShowTalk` | legado v0.11 — ShowTalk sempre patchado (risco de lag no mapa) |

### Modos de boot (teste)

| Config | Efeito |
|--------|--------|
| `UltraShellMode = true` | Load vazio — zero Harmony/tradução |
| `ShellMode = true` | init leve — sem PatchAll |
| `TalkEnabled = false` | sem Falar/Google/npctalk |
| `LabelsEnabled` | reservado — `UiTextPatch` desligado |

### Classe alvo principal

- **`UIInteractionController`** — patches de diálogo, menu fancy, interação
- **`UISelectionController`** — clique/seleção no mundo (base; `UIInteractionController` herda)
- **`AreaCmdProcessor_NewGui`** (global) — `ProcessPreTalkScreen`, `ProcessEndInteraction`, `ProcessStartInteraction` (podem estar **fora** de `UIInteractionController`)

---

## Mapa completo de hooks

### 1. Clique / seleção no mundo (`InteractionClickPatch`)

**Tipos:** `UISelectionController`, `UIInteractionController`

| Método jogo | Postfix plugin | O que faz |
|-------------|----------------|-----------|
| `OnNewTarget` | `UiSelectionPostfix` | clique no NPC — inicia captura Interacao |
| `InvokeOnNewTarget` | `UiSelectionPostfix` | idem |
| `UpdateInteractButton` | `UiSelectionPostfix` | idem |
| `MouseOver` | `UiSelectionPostfix` | hover com arg UISelection |

**Fluxo `UiSelectionPostfix`:**

```
Resolve(__instance) → Remember controller
CaptureFromSelection(selection)
FinishInteractionCapture(label, controller)
  → InteractionSubtextCapture (5 camadas)
  → log Interacao + Consultar npc json
```

### 2. Painel de interação (`InteractionClickPatch` + `TalkScreenPatch`)

**Tipo:** `UIInteractionController` (+ overloads em `UISelectionController`)

| Método jogo | Postfix | O que faz |
|-------------|---------|-----------|
| `UpdateInteractorName` (todas overloads) | `InteractionPanelPostfix` | Remember + probe subtexto |
| `UpdateInteractorName` (1 string) | `UpdateInteractorNameStringPostfix` | nome + entity + probe |
| `UpdateInteractorName` (int + string) | `UpdateInteractorNameEntityStringPostfix` | idem com EntityID |
| `UpdateInteractionPanel` | `InteractionPanelPostfix` | probe subtexto |
| `UpdateInteractorDesc` | `InteractionPanelPostfix` | probe subtexto |
| `UpdateInteractorSubtext` | `InteractionPanelPostfix` | probe subtexto |
| `RefreshInteractionUI` | `InteractionPanelPostfix` | probe subtexto |
| `SetInteractorLabel` | `InteractionPanelPostfix` | probe subtexto |
| `ShowInteractMenu` | `InteractionPanelPostfix` | probe subtexto |
| `UpdatePreTalkScreen` | `InteractionPanelPostfix` | probe subtexto |
| `UpdateFancyMenu` | `UpdateFancyMenuPostfix` | menu + probe |
| `UpdateFancyMenuOptions` | `UpdateFancyMenuOptionsPostfix` | opções + probe |
| `OnPreTalk` | `OnPreTalkPostfix` | PreTalkScreenInfo + probe |

### 3. Sessão de diálogo (`TalkScreenPatch.PatchSessionHooks`)

| Método jogo | Prefix/Postfix | O que faz |
|-------------|----------------|-----------|
| `ProcessStartInteraction` | prefix + postfix | captura npcId, `PrimeFromPack` |
| `ProcessSelectEntity` | prefix + postfix | entityId + painel |
| `ProcessPreTalkScreen` | `SessionOpenPrefix` | abre sessão, aplica patch dinâmico ShowTalk |
| `ProcessEndInteraction` | `SessionClosePostfix` | fecha sessão, remove patch dinâmico |
| `ShowTalk` | `ShowTalkPrefix` | traduz falas/botões (dinâmico ou always) |
| `DisplayTalkScreen` | prefix tradução | aplica cache na UI |
| `ProcessTalkScreen` | prefix (estratégia alternativa) | idem via outro hook |

### 4. Debug probes (config Diagnostic)

| Probe | Config | Log |
|-------|--------|-----|
| `SelectionDebugProbe` | `SelectionDebug = true` | `etapa DebugSelecao` |
| `NpcIdDebugProbe` | `NpcIdDebug = true` | `etapa DebugNpcId` |
| `InteractionSubtextCapture` | `SubtextDebug = true` | `etapa DebugSubtexto` |
| `TalkDiagnostics` | `Diagnostic.Enabled = true` | `hook-stats.log` |

---

## Pipeline de etapas (TalkLog)

Ordem lógica que o plugin segue:

```
1. Interacao          ← clique no NPC (sempre loga, mesmo sem TalkVerbose)
2. Consultar npc json ← strings_npcregistry.json / pack CDN
3. Subtexto           ← captura separada (InteractionSubtextCapture)
4. Falar              ← abrir diálogo
5. Coletar            ← ShowTalk / ProcessTalkScreen
6. Consultar json     ← strings_npctalk.json / npc talk pack
7. Google             ← se não achar local
8. Aplicar na tela    ← DisplayTalkScreen / cache TalkTurn
```

**Fluxos documentados:**

- JSON: `Falar > Coletar > Consultar json > Aplicar na tela`
- Google: `Falar > Coletar > Consultar json > Google > Aplicar na tela`

---

## Lógica de Interacao + subtexto

### TalkSession (estado do clique)

| Campo | Uso |
|-------|-----|
| `NpcId` | ex. `NPC_Barbran` — vem de `ProcessStartInteraction` ou cache |
| `NpcName` | nome exibido — normaliza `Nameplate: X` → `X` |
| `NpcSubtext` | descrição cyan do painel |
| `LastSelection` | objeto `UISelection` do clique |
| `SelectedEntityId` | EntityID do jogo |
| `Active` | true durante diálogo aberto |

**Reset:** `BeginInteractionCapture()` no clique limpa id/nome/subtexto.  
**Fim diálogo:** `Close()` limpa tudo + `UiInteractionFinder.ClearCache()`.

### InteractionSubtextCapture — 5 camadas

Probe dispara em: `FinishInteractionCapture`, `UpdateInteractorName`, `UpdateFancyMenu*`, `InteractionPanel*`, `OnPreTalk`.

| Camada | ID log | Fonte |
|--------|--------|-------|
| 1 | `1-UISelection` | `UISelection` — clique (name, EntityID, campos desc) |
| 2 | `2-UIInteractionController` | strings do controller (`fonte=hook\|cache\|finder`) |
| 3 | `3-PreTalkScreenInfo` | `pendingPreTalkScreenInfo.*` |
| 4 | `4-UIInteractionController.ui` | refs UI (`FancyMenuHeading`, `InteractorName`, …) + TMP |
| 5 | `5-UIInteractionController.scanTMP` | árvore TMP completa (até 24 textos) |

**Classificação de campos no log:**

| Tag | Significado |
|-----|-------------|
| `(vazio)` | string null/blank |
| `(nome)` | nameplate / nome do NPC |
| `(ui)` | ruído UI (`Talk`, `Entity Name Here`) |
| `(curto)` | &lt; 8 chars |
| `(texto)` | string longa mas não candidata |
| `★` | **candidato a subtexto** — usado para captura |

**Regra de captura:** maior candidato `★` vence → `TalkSession.SetSubtext` → `NotifySubtextCaptured` → atualiza registry se Interacao já logou.

### UiInteractionFinder

Ordem de resolução do `UIInteractionController`:

1. `__instance` do hook (se `IsInstanceOfType`)
2. scan de campos do hook (depth 2)
3. cache (`Remember` nos hooks)
4. `FindObjectOfType` / `FindObjectsOfType` / `Resources.FindObjectsOfTypeAll`

---

## Stores e arquivos JSON

| Store | Arquivo | Quando |
|-------|---------|--------|
| `NpcRegistryStore` | `Translation/strings_npcregistry.json` | Interacao — nome + subtexto EN descobertos |
| `NpcTalkStore` | `Translation/strings_npctalk.json` | Falar — falas traduzidas |
| `CdnStringStore` | `strings_*.json` do pack | lookup CDN (itens, npcs oficiais) |
| `EntityNpcIdCache` | memória | entityId/nome → npcId após 1º Talk |

**Pasta Translation (Proton/Linux):**

```
~/.steam/.../compatdata/342940/pfx/drive_c/users/steamuser/AppData/LocalLow/
  Elder Game/Project Gorgon/Translation/
```

---

## Configuração recomendada (teste subtexto)

Arquivo: `BepInEx/config/com.pg.translatelive.cfg`

```ini
[Hooks]
PatchStrategy = DynamicShowTalk
TalkEnabled = true

[Diagnostic]
SelectionDebug = true
NpcIdDebug = true
SubtextDebug = true

[NpcTalk]
SaveNewNpcRegistry = true
```

Build/install:

```bash
make build-plugin install-plugin
```

---

## Monitoramento de log

```bash
tail -n 0 -f ~/.steam/debian-installation/steamapps/common/Project\ Gorgon/BepInEx/LogOutput.log \
  | grep -iE "DebugSubtexto|Subtexto|Interacao|Consultar npc|UiInteractionFinder|UpdateInteractorName|UpdateInteractionPanel"
```

**Boot:** `Pg Translate Live 0.14.30`

**Teste A — selecionar NPC:** `hook=FinishInteractionCapture` + 5 camadas  
**Teste B — abrir Talk:** `hook=UpdateInteractorName` + camadas 4–5 com TMP

Arquivos auxiliares:

| Arquivo | Conteúdo |
|---------|----------|
| `BepInEx/LogOutput.log` | log principal |
| `BepInEx/plugins/PgTranslateLive/hook-stats.log` | contadores (Diagnostic.Enabled) |
| `BepInEx/plugins/PgTranslateLive/trace.log` | trace detalhado (Trace.Enabled) |
| `BepInEx/plugins/PgTranslateLive/discovery.log` | discovery mode |

---

## O que NÃO está ativo nesta versão

- **UiTextPatch** — desligado (v0.13.5) — evita lookup CDN a cada frame
- **NpcYamlStore** — substituído por `NpcTalkStore` + JSON
- **AlwaysShowTalk** — só fallback ou teste explícito
- **PackSync Supabase** — planejado (ver `FLOW.md`)

---

## Histórico desta versão (v0.14.30)

- Código unificado em `src/main.cs`
- Pipeline **Interacao → Consultar npc json → Subtexto → Falar**
- Probe subtexto em **5 camadas** com log `DebugSubtexto`
- `UiInteractionFinder.Resolve(__instance)` no clique
- Hooks extras: `UpdateInteractionPanel`, `UpdateInteractorDesc`, `UpdateInteractorSubtext`, …
- `NpcRegistryStore` para NPCs descobertos no clique
- `DynamicShowTalk` como estratégia padrão de performance

---

## Referências no repo

| Doc | Conteúdo |
|-----|----------|
| `FLOW.md` | visão geral para usuário |
| `DIAGNOSTIC.md` | travadas, ProcessPreTalkScreen em outra classe |
| `INTEGRATION.md` | fusão futura com Translator.dll |
| `BACKUP-v0.14.30.md` | **este arquivo** |
