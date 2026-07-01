# Regra: logs do PgTranslateLive — NUNCA remover

**Obrigatório ler esta regra antes de editar `main.cs` ou qualquer código de logging do plugin.**

## Regra principal

**NUNCA remova, comente, simplifique ou “limpe” logs de diagnóstico sem pedido explícito do usuário.**

Isso inclui refactors que “só reorganizam” mas apagam chamadas de log, mensagens ou etapas do pipeline.

## Logs protegidos (não apagar)

| Etapa / mensagem | Onde vive |
|------------------|-----------|
| `etapa Interacao` (incl. `\| subtexto:` inline) | `TalkLog.Interacao`, `TalkSession.LogInteraction` |
| `etapa DebugSubtexto` (camadas 1–5, hook=…) | `TalkLog.DebugSubtexto`, `InteractionSubtextCapture` |
| `etapa Subtexto` | `TalkLog.Subtexto` |
| `etapa Consultar npc json` | `TalkLog.NpcJsonChecking` |
| `npc ja existe no json` | `TalkLog.NpcJsonFound` |
| `nao tem no json` | `TalkLog.NpcJsonMissing` |
| `etapa DebugSelecao` | `TalkLog.DebugSelecao` |
| `etapa DebugNpcId` | `TalkLog.DebugNpcId` |
| Pipeline Falar (`etapa Falar`, `Coletar`, `Consultar json`, etc.) | `TalkLog.Pipeline` |

## Código protegido (não deletar “por performance”)

- `InteractionSubtextCapture` — captura e probe de subtexto no clique
- `TalkSession.FinishInteractionCapture` — dispara Interacao + Consultar npc json
- `PreTalkScreenProbe` — leitura de contexto NPC no painel
- Flags em `PluginSettings`: `SubtextDebug`, `SelectionDebug`, `NpcIdDebug` (default **true**)

## Como reduzir ruído (permitido)

Use **config** (`dist/com.pg.translatelive.cfg` ou cfg do jogo), **não** remoção de código:

```ini
[Diagnostic]
SubtextDebug = false
SelectionDebug = false
NpcIdDebug = false
```

Ou `TalkVerbose` / `TranslateClient.LogVerbose` para o pipeline Falar.

## O que fazer ao refatorar

1. Ler esta regra.
2. Manter todas as chamadas de log existentes (ou pedir ao usuário antes de mudar).
3. Se precisar mudar formato, **adicionar** — não substituir silenciosamente.
4. Buscar em `main.cs` por `LogPolicy` / comentários `RULE.md` antes de commitar.

## Referência no código

`main.cs` aponta para este arquivo via `LogPolicy.RulePath` e comentários `LogPolicy.Reminder` nos pontos críticos.

## Pasta Translation/ única (runtime)

Plugin lê e grava **somente** em `GameDataPaths.TranslationDir`:

- Proton/Linux: `compatdata/342940/.../LocalLow/Elder Game/Project Gorgon/Translation/`
- Override opcional: `[Paths] TranslationDir` na cfg
- `output/Translation/` no repo = pack para **instalar** (não é lido pelo plugin ao jogar)
- Instalação copia para o caminho canônico; **preserva** `strings_npctalk.json` existente no jogo

## strings_npctalk.json — só diálogo Falar

- **Conteúdo:** frases do `ShowTalk` / botões de escolha (EN exato → PT).
- **Não incluir:** subtexto do painel (`npcinfo_*_Desc` → `strings_npcs.json`), prefs de vendor, nomes soltos, coords legado `npcs.yaml`.
- **Runtime:** `NpcTalkStore.IsEligibleTalkEntry` filtra gravações; subtexto **não** vai para npctalk (`ResolveSubtext`).
- **Limpar pack:** `python3 scripts/clean-strings-npctalk.py`
