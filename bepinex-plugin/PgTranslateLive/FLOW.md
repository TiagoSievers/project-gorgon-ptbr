# Pg Translate Live — como funciona

Guia simples: ordem do jogo, arquivos do plugin e o que cada um faz.

---

## Estrutura de pastas (`src/`)

Todo o código C# está unificado em **`src/main.cs`** (~6000 linhas), organizado por `#region`:

| Região | Conteúdo original |
|--------|-------------------|
| **Config** | PluginSettings, PatchStrategy |
| **Paths** | GameDataPaths |
| **Logging** | LogAscii, TraceLog, TalkLog |
| **Il2Cpp** | Il2CppStringHelper, Il2CppArrayReflection |
| **Stores** | CdnStringStore, NpcTalkStore, NpcRegistryStore |
| **Translate** | TranslateClient, GoogleTranslate, GoogleAsyncQueue |
| **Npc** | NpcNameResolver, EntityNpcIdCache |
| **Talk** | patches, sessão, diálogo, discovery |
| **Interaction** | InteractionClickPatch, UiInteractionFinder |
| **Ui** | UiTextPatch |
| **Debug** | probes, TalkDiagnostics |
| **Plugin** | entrada BepInEx (`Load()`) |

---

## Quando você abre o jogo

```
Steam inicia o jogo (Proton)
    │
    ▼
BepInEx carrega as DLLs
    │
    ▼
Plugin.Load()  ← plugin liga aqui (rápido, não trava)
    │
    ▼
Unity lê GorgonSettings.txt
    │
    ▼
Jogo lê a pasta Translation/  ← textos em PT se o pack já estiver no PC
    │
    ▼
Tela de login / escolher personagem
    │
    ▼
Entrar no mundo
```

**Importante:** o pack de tradução (menu, itens, quests) precisa **já estar na pasta** quando o jogo sobe. Se baixar na primeira vez, feche e abra o jogo de novo.

---

## Arquivos do plugin — o que cada um faz

| Arquivo | O que faz |
|---------|-----------|
| **Plugin.cs** | Ponto de entrada. Quando o jogo abre, liga o plugin e aplica os patches de diálogo. |
| **TalkScreenPatch.cs** | “Gancho” no jogo: intercepta a tela **Falar** com NPC. |
| **TextPatchHelper.cs** | Pega o texto inglês do diálogo e manda traduzir antes de mostrar na tela. |
| **TranslateClient.cs** | Decide se traduz, tamanho mínimo do texto, quantas threads usar. Lê o `.cfg`. |
| **GoogleTranslate.cs** | Chama a API do Google Translate pela internet. |
| **TalkTurn.cs** | Guarda a tradução da conversa atual para não traduzir a mesma frase duas vezes. |
| **NpcYamlStore.cs** | Consulta `npcs.yaml` antes do Google; salva falas novas após traduzir. |
| **TalkLog.cs** | Escreve no log quando você usa **Falar** (útil para debug). |
| **Il2CppStringHelper.cs** | Ajuda a ler/escrever texto dentro do Unity (IL2CPP). |
| **LogAscii.cs** | Log sem acentos quebrados no Proton/Linux. |

**Ainda não existe (planejado):**

| Arquivo (futuro) | O que fará |
|------------------|------------|
| **PackSyncRunner.cs** | No 1º frame após abrir o jogo: vê se há pack novo no Supabase e baixa. |
| **PackSyncConfig.cs** | URL do Supabase e ligar/desligar o download automático. |

---

## Duas formas de tradução

### 1. Textos fixos do jogo (menu, itens, quests)

- **Quem traduz:** você (pipeline) ou download automático (futuro Supabase).
- **Onde fica no PC:** pasta `Translation/` dentro dos dados do jogo.
- **Quando o jogo usa:** ao abrir — login, inventário, tooltips, etc.
- **Plugin hoje:** não baixa nada; só o jogo lê os arquivos que já estão no disco.

### 2. Diálogo **Falar** com NPC (ao vivo)

- **Quem traduz:** plugin — primeiro consulta `npcs.yaml`, só chama Google se a fala ainda não estiver lá.
- **Quando:** só quando você clica **Falar** num NPC.
- **Fluxo:**

```
Você clica Falar
    → TalkScreenPatch pega o texto
    → NpcYamlStore: já tem em npcs.yaml? → usa PT local (sem internet)
    → senão → GoogleTranslate traduz → salva em npcs.yaml para a próxima vez
    → TalkTurn guarda e mostra em PT na tela
```

**Sincronizar com o repo (mantenedor):**

```bash
./scripts/merge-npcs-from-game.sh
# mescla .../Translator/translations/pt-BR/npcs.yaml → output/pt-BR/npcs.yaml
```

---

## Onde fica a pasta Translation/

O jogo procura aqui (Proton/Linux, exemplo):

```
~/.steam/.../compatdata/969170/pfx/.../AppData/LocalLow/
  Elder Game/Project Gorgon/Translation/
```

Instalação manual (sem plugin): `make install-official` no projeto Python.

---

## Logs e configuração

| Onde | Para quê |
|------|----------|
| `Project Gorgon/BepInEx/LogOutput.log` | Mensagens do plugin |
| `.../Project Gorgon/Player.log` | Log do Unity/jogo |
| `BepInEx/config/com.pg.translatelive.cfg` | Ligar Google, verbose, etc. |
