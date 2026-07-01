# Pg Translate Live — diagnóstico de travadas no mapa

Documento de referência para **não repetir** análises já feitas.  
Última atualização: **2026-06-27** (v0.12.1 + hook-stats + **teste A/B definitivo**).

---

## Arquitetura de tradução (referência)

| Camada | Onde in-game | Conteúdo |
|--------|--------------|----------|
| **CDN (language pack)** | `Translation/` (AppData / `~/.config/unity3d/...`) | UI, itens, quests — **única** cópia estática |
| **PgTranslateLive** | `BepInEx/plugins/PgTranslateLive/` | Falar ao vivo (Google); `npcs.yaml` local opcional |
| **Pipeline** | `output/Translation/` (repo) | Gera pack CDN — **não** duplicar em `plugins/Translator/` |

**Legado removido:** mod PickTeam `Translator.dll` + YAML em `BepInEx/plugins/Translator/` (duplicava CDN).

---

## Sintoma reportado

- Micro-travadas rítmicas (~3 s) **andando no mapa** (todos os mapas).
- Ocorre **com Pg Translate Live ligado**; usuário reporta que **só Translator** funciona bem.
- Reproduzido em **Linux (Proton)** e **Windows nativo** — não é exclusivo de Proton.
- Objetivo do plugin: tradução ao vivo em **Falar** com NPC, sem impacto no mundo aberto.

---

## O que já foi descartado

| Hipótese | Teste | Resultado |
|----------|-------|-----------|
| Patch em `DisplayTalkScreen` + `ref string` | Removido v0.11 | Travada continuou (mas v0.12.0 caía em fallback — ver abaixo) |
| Patch em `UpdateFancyMenuOptions` | Removido v0.10.1 | Travada continuou |
| `NpcYamlStore` load no boot | Desligado v0.10.3 | Travada continuou |
| Google sync na thread principal | Async v0.10.2 | Travada continuou |
| `ShowTalk` chamado a cada ~3 s **no mapa** | **Modo diagnóstico v0.12.1** | **REFUTADO** — ver seção hook-stats |
| Travada só em área populada | Usuário: todos os mapas | Não explica sozinha; appearance loop do jogo existe mas hooks = 0 no mapa |
| “Dois plugins Harmony juntos” causam stutter | **Teste A/B #2** | **REFUTADO** — PgTranslateLive **sozinho** já trava |
| Translator.dll causa stutter | **Teste A/B #1** | **REFUTADO** — só Translator = sem travada |
| Harmony + `PatchAll` / `IndexMethodsGlobally` no boot | **Teste #5 shell** (2026-06-27 23:07) | **REFUTADO** — `ShellMode=true`, sem Harmony; **travada continuou** |

---

## Evidência principal: `hook-stats.log`

**Arquivo:** `BepInEx/plugins/PgTranslateLive/hook-stats.log`  
**Cfg:** `[Diagnostic] Enabled = true`, `IntervalSec = 10`  
**Sessão:** 2026-06-27 22:28:43 → 22:31:33

### Andando no mapa (~100 s, sem Falar)

```
22:28:53 → 22:30:23  ShowTalk=0(+0)  ProcessTalkScreen=0(+0)
                       ProcessPreTalkScreen=0(+0)  ProcessStartInteraction=0(+0)
```

**10 intervalos × 10 s = zero chamadas** em todos os hooks de diálogo.

**Conclusão:** os métodos patchados (`ShowTalk`, `ProcessTalkScreen`, etc.) **não rodam** enquanto o jogador só anda. A travada rítmica no mapa **não é** causada por código do prefix Harmony nesses métodos (não há invocações).

### Falar com NPC (22:30:33+)

```
22:30:33  ShowTalk=1(+1)   ProcessTalkScreen=1(+1)   ProcessPreTalkScreen=1(+1)
22:30:43  ShowTalk=2(+1)
22:30:53  ShowTalk=3(+1)
22:31:03  ShowTalk=4(+1)
22:31:13+ ShowTalk=4(+0)
```

Hooks disparam **1× por linha de diálogo**, como esperado. `ProcessEndInteraction=0` nessa sessão (diálogo possivelmente não fechado antes de sair).

---

## Bug encontrado na v0.12.0 (corrigido v0.12.1)

### Log (`BepInEx/LogOutput.log`)

```
ProcessPreTalkScreen/ProcessEndInteraction nao encontrados — fallback AlwaysShowTalk.
```

**Causa:** `ProcessPreTalkScreen` e `ProcessEndInteraction` **não estão** em `UIInteractionController`. Ficam em **outra classe** do jogo. A v0.12.0 só buscava na classe errada → patch dinâmico falhava → voltava ao modo **AlwaysShowTalk** (ShowTalk patchado permanentemente).

**Correção v0.12.1:** busca global em todas as classes (`IndexMethodsGlobally`), ignorando interfaces/métodos abstratos.

**Nota:** mesmo com AlwaysShowTalk, o hook-stats provou que **ShowTalk não é invocado no mapa**. O fallback explica confusão nos testes v0.12.0, mas **não** explica travada periódica por si só (sem chamadas = sem prefix executando).

---

## `Player.log` (jogo vanilla)

- Muitas linhas `Download appearance loop @Base2-m(...)` (~1–3 s em rajadas).
- Atividade Unity/servidor (aparência de personagens), **não** log do Pg Translate Live.
- Pode contribuir para sensação de stutter; **independente** dos hooks de diálogo (contador = 0 no mapa).

---

## Arquitetura atual (v0.12.1)

| Modo | Comportamento |
|------|----------------|
| `PatchStrategy = DynamicShowTalk` (padrão) | Patch tradução em `ShowTalk` só entre `ProcessPreTalkScreen` e `ProcessEndInteraction` |
| `PatchStrategy = ProcessTalkScreen` | Hook alternativo no pipeline de diálogo |
| `PatchStrategy = AlwaysShowTalk` | Legado v0.11 — ShowTalk patchado no boot |
| `Diagnostic.Enabled = true` | Só contadores (sem traduzir); grava `hook-stats.log` |

**Boot:** scan `AccessTools.AllTypes()` gera muitos warnings HarmonyX no `LogOutput.log` (custo one-shot na carga, não periódico).

---

## Teste A/B definitivo (2026-06-27)

Cada teste: **mover/remover** a DLL da pasta `BepInEx/plugins/` (não usar `.bak` na mesma pasta). Andar no mapa ~2 min.

| # | Config | Trava no mapa? | Resultado |
|---|--------|----------------|-----------|
| 1 | **Só `Translator.dll`** (sem PgTranslateLive) | **Não** | Baseline OK |
| 2 | **Só `PgTranslateLive.dll`** (sem Translator) | **Sim** | **Culpado confirmado: Pg Translate Live** |
| 5 | **Shell** (`ShellMode=true`, sem Harmony/PatchAll) | **Sim** | Harmony/scan boot **não** causam stutter no mapa |
| 3 | Ambos + `DynamicShowTalk` | *(pendente)* | — |
| 4 | Ambos + `PatchStrategy = ProcessTalkScreen` | *(pendente)* | — |

### Conclusão do A/B

- A travada **não depende** do Translator estar presente — **PgTranslateLive sozinho basta** para reproduzir.
- A travada **não é** efeito colateral exclusivo de “dois plugins Harmony juntos” (Translator isolado = OK).
- Combinado com **hook-stats = 0 chamadas no mapa**, a causa **não é** prefix de `ShowTalk`/`ProcessTalkScreen` rodando enquanto se anda.
- **Teste #5 (shell):** sem Harmony, sem `PatchAll`, sem scan global — **travada continuou**. Causa **não é** patches Harmony nem `IndexMethodsGlobally` no boot.
- Suspeita atual: **só ter `PgTranslateLive.dll` carregada no processo BepInEx** (assembly + init mínimo do `Load()`), ou efeito colateral da carga da DLL (JIT/refs estáticas). Mecanismo periódico (~3 s) **ainda não identificado** no código do plugin (shell não inicia timers, threads nem hooks).

**Próximo foco:** plugin **ultra-shell** (`Load()` vazio, sem `TranslateClient`/`NpcYamlStore`), ou **fundir no Translator.dll** (`INTEGRATION.md` — Translator sozinho = OK).

---

## Testes ainda pendentes

| # | Config | Pergunta |
|---|--------|----------|
| 3 | Ambos + `DynamicShowTalk` + `Diagnostic.Enabled = false` | Trava? Falar traduz? |
| 4 | Ambos + `PatchStrategy = ProcessTalkScreen` | Mapa OK? Falar OK? |
| 5 | PgTranslateLive **shell** (`ShellMode=true`) | **Trava** — Harmony/scan **descartados** |
| 6 | **Ultra-shell** (`UltraShellMode=true`, Load vazio) | Trava? — isola assembly vs init leve |

**Teste #6 (ultra-shell):**
1. **Só** `PgTranslateLive.dll` — mover `Translator.dll` para fora de `BepInEx/plugins/`.
2. Em `com.pg.translatelive.cfg`: `UltraShellMode = true`, `ShellMode = false`, `Trace Enabled = false`.
3. Log esperado: `ULTRA-SHELL: Load vazio — zero TranslateClient/NpcYaml/Harmony`.
4. Andar no mapa ~2 min.
   - **Sem travada** → culpado = init leve do shell (`HttpClient`, `NpcYamlStore.ResolvePath`, etc.).
   - **Com travada** → culpado = **presença da assembly** → `INTEGRATION.md` (fundir no Translator).

**Importante:** desabilitar plugin = **mover/remover** `PgTranslateLive.dll`, não renomear para `.bak` na mesma pasta (BepInEx ignora `.bak`).

---

## Config de referência pós-diagnóstico

```ini
[General]
Enabled = true
UseGoogle = true

[Hooks]
PatchStrategy = DynamicShowTalk
ShellMode = false   # true = sem Harmony (init leve)
UltraShellMode = false   # true = Load vazio (teste #6)

[Diagnostic]
Enabled = false   # true só para coletar hook-stats.log
IntervalSec = 10

[Trace]
Enabled = true    # trace detalhado do plugin em trace.log
VerboseHooks = false
SnapshotIntervalSec = 3

[NpcYaml]
UseLookup = false
SaveNewTranslations = false
```

---

## Linha do tempo de versões (stutter)

| Versão | Mudança relevante |
|--------|-------------------|
| v0.10.1 | Remove `UpdateFancyMenuOptions` |
| v0.10.2 | Google async |
| v0.10.3 | Sem yaml boot |
| v0.10.4 | `ApplyTalkPatches` teste (removido depois) |
| v0.11.0 | Só `ShowTalk`; remove DisplayTalkScreen/binder |
| v0.12.0 | Patch dinâmico + diagnóstico — **fallback AlwaysShowTalk** (bug busca de classe) |
| v0.12.1 | Busca global ProcessPreTalk/EndInteraction + fix fallback |

---

## Regra para futuras análises

> **Se `hook-stats.log` mostrar `ShowTalk(+0)` por 60+ s andando no mapa, não reabrir investigação de “hook quente em ShowTalk”.**

> **Teste A/B (2026-06-27): só Translator = OK; só PgTranslateLive = trava.** Não culpar o Translator nem “dois plugins” como causa primária.

> **Teste #5 shell:** sem Harmony/PatchAll, travada continuou. **Não reabrir** Harmony, `PatchAll`, `IndexMethodsGlobally` ou hooks de diálogo como causa do stutter no mapa. Investigar **assembly carregada** ou init mínimo restante; próximo = ultra-shell ou integração no Translator.

---

## Trace detalhado (instrumentação atual)

Foi adicionada instrumentação em arquivo próprio para investigar o que o PgTranslateLive faz sem depender do `LogOutput.log` do BepInEx.

**Arquivo:** `BepInEx/plugins/PgTranslateLive/trace.log`

**Config recomendada para teste:**

```ini
[Trace]
Enabled = true
VerboseHooks = false
SnapshotIntervalSec = 3
```

### O que o trace mede

- Fases de `Plugin.Load()`
- `PluginSettings.Init`, `TranslateClient.Init`, `NpcYamlStore.Init`
- `TalkScreenPatch.PatchAll`
- `AccessTools.TypeByName`
- `AccessTools.AllTypes()` / busca global de métodos
- Quantidade de tipos, métodos e matches encontrados
- `Harmony.Patch` / `Harmony.Unpatch`
- `SessionOpenPrefix` / `SessionClosePostfix`
- `TalkDynamicPatch.ApplyTranslationPatches`
- `TalkDynamicPatch.RemoveTranslationPatches`
- `TranslateClient.TryTranslateBatch`
- `NpcYamlStore.TryGet`, `Record`, `Reload`, `LoadFile`, `WriteFile`
- `GoogleAsyncQueue.EnqueueBatch` / `RunOne`
- Contadores e duração máxima por operação em snapshots periódicos

### Como interpretar

- Linhas `*.begin` / `*.end Xms` indicam duração da operação.
- Linhas `counter nome=valor` mostram acumulados desde o boot do plugin.
- Linhas `max nome=Xms` mostram o maior tempo observado para aquela operação.
- Se houver travada no mapa sem hooks de diálogo, procurar no trace por operações que continuam aparecendo nos snapshots ou por tempos altos fora do fluxo **Falar**.

### Cuidado

`VerboseHooks=false` deve permanecer assim no teste normal. `VerboseHooks=true` grava uma linha por chamada de hook e deve ser usado só em teste curto, pois pode aumentar I/O.

---

## Arquivos úteis

| Arquivo | Conteúdo |
|---------|----------|
| `BepInEx/plugins/PgTranslateLive/hook-stats.log` | Contadores de hooks (modo diagnóstico) |
| `BepInEx/plugins/PgTranslateLive/trace.log` | Trace detalhado do PgTranslateLive |
| `BepInEx/LogOutput.log` | Boot BepInEx, warnings Harmony, fallback |
| `.../Project Gorgon/Player.log` | Unity, appearance loop |
| `BepInEx/config/com.pg.translatelive.cfg` | Estratégia de patch e diagnóstico |
| `FLOW.md` | Fluxo normal do plugin |
| `INTEGRATION.md` | Plano fundir com Translator.dll |
