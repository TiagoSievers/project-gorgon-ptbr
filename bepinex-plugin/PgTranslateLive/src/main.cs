// PgTranslateLive — unified source (main.cs)
// LogPolicy.Reminder — leia RULE.md antes de alterar qualquer log.

using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BepInEx;
using HarmonyLib;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace PgTranslateLive;

/// <summary>
/// Política de logs — ver <see cref="LogPolicy.RulePath"/>.
/// NUNCA remover logs de diagnóstico sem pedido explícito do usuário.
/// </summary>
internal static class LogPolicy
{
    internal const string RulePath = "bepinex-plugin/PgTranslateLive/src/RULE.md";
    internal const string Reminder =
        "NUNCA remover logs — ver bepinex-plugin/PgTranslateLive/src/RULE.md";
}

#region Config
// --- PluginSettings.cs ---
internal enum PatchStrategy
{
    /// <summary>ShowTalk patch só durante diálogo (ProcessPreTalkScreen → ProcessEndInteraction).</summary>
    DynamicShowTalk,

    /// <summary>Hook em ProcessTalkScreen em vez de ShowTalk (teste alternativo).</summary>
    ProcessTalkScreen,

    /// <summary>v0.11 — ShowTalk patchado o tempo todo (pode travar no mapa).</summary>
    AlwaysShowTalk,
}

internal static class PluginSettings
{
    private static ConfigEntry<bool> _diagnosticMode = null!;
    private static ConfigEntry<int> _diagnosticIntervalSec = null!;
    private static ConfigEntry<PatchStrategy> _patchStrategy = null!;
    private static ConfigEntry<bool> _shellMode = null!;
    private static ConfigEntry<bool> _ultraShellMode = null!;
    private static ConfigEntry<bool> _talkEnabled = null!;
    private static ConfigEntry<bool> _labelsEnabled = null!;
    private static ConfigEntry<bool> _traceEnabled = null!;
    private static ConfigEntry<bool> _traceVerboseHooks = null!;
    private static ConfigEntry<int> _traceSnapshotIntervalSec = null!;
    private static ConfigEntry<bool> _discoveryMode = null!;
    private static ConfigEntry<int> _discoveryIntervalSec = null!;
    private static ConfigEntry<int> _discoveryMaxMethods = null!;
    private static ConfigEntry<bool> _selectionDebug = null!;
    private static ConfigEntry<bool> _npcIdDebug = null!;
    private static ConfigEntry<bool> _subtextDebug = null!;
    private static ConfigEntry<string> _translationDirOverride = null!;

    internal static bool ShellMode => _shellMode?.Value ?? false;
    internal static bool UltraShellMode => _ultraShellMode?.Value ?? false;
    internal static bool TalkEnabled => _talkEnabled?.Value ?? true;
    internal static bool LabelsEnabled => _labelsEnabled?.Value ?? true;
    internal static bool LabelsOnly => LabelsEnabled && !TalkEnabled;
    internal static bool DiagnosticMode => _diagnosticMode?.Value ?? false;
    internal static int DiagnosticIntervalSec => _diagnosticIntervalSec?.Value ?? 10;
    internal static bool DiscoveryMode => _discoveryMode?.Value ?? false;
    internal static int DiscoveryIntervalSec => _discoveryIntervalSec?.Value ?? 5;
    internal static int DiscoveryMaxMethods => _discoveryMaxMethods?.Value ?? 400;
    internal static PatchStrategy Strategy => _patchStrategy?.Value ?? PatchStrategy.DynamicShowTalk;
    internal static bool TraceEnabled => _traceEnabled?.Value ?? false;
    internal static bool TraceVerboseHooks => _traceVerboseHooks?.Value ?? false;
    internal static int TraceSnapshotIntervalSec => _traceSnapshotIntervalSec?.Value ?? 3;
    internal static bool SelectionDebug => _selectionDebug?.Value ?? false;
    internal static bool NpcIdDebug => _npcIdDebug?.Value ?? false;
    internal static bool SubtextDebug => _subtextDebug?.Value ?? true;
    /// <summary>Pasta Translation/ única — vazio = auto (Proton LocalLow 342940).</summary>
    internal static string TranslationDirOverride => _translationDirOverride?.Value?.Trim() ?? "";

    internal static void Init(ConfigFile config)
    {
        _diagnosticMode = config.Bind("Diagnostic", "Enabled", false,
            "Conta chamadas de hooks e grava hook-stats.log a cada N segundos (sem traduzir).");
        _diagnosticIntervalSec = config.Bind("Diagnostic", "IntervalSec", 10,
            "Intervalo em segundos entre snapshots em hook-stats.log.");
        _discoveryMode = config.Bind("Discovery", "Enabled", false,
            "Patch metodos de interacao por padrao de nome; grava discovery.log (sem traduzir).");
        _discoveryIntervalSec = config.Bind("Discovery", "IntervalSec", 5,
            "Intervalo em segundos entre snapshots em discovery.log.");
        _discoveryMaxMethods = config.Bind("Discovery", "MaxMethods", 400,
            "Limite de metodos patchados no discovery (performance).");
        _patchStrategy = config.Bind("Hooks", "PatchStrategy", PatchStrategy.DynamicShowTalk,
            "DynamicShowTalk = patch ShowTalk só no diálogo | ProcessTalkScreen = hook alternativo | AlwaysShowTalk = legado v0.11.");
        _talkEnabled = config.Bind("Hooks", "TalkEnabled", true,
            "Falar com NPC (ShowTalk/Google/npctalk). false = modo so rotulos CDN (leve).");
        _labelsEnabled = config.Bind("Hooks", "LabelsEnabled", true,
            "UiTextPatch: hook UITools.SetText / TMP_Text para traduzir UI estatica via CdnStringStore (ex.: login, rotulos).");
        _shellMode = config.Bind("Hooks", "ShellMode", false,
            "Teste: true = Load sem Harmony/PatchAll (init leve). Traducao desligada.");
        _ultraShellMode = config.Bind("Hooks", "UltraShellMode", false,
            "Teste: true = Load vazio (so config + log). Sem TranslateClient/NpcTalk/Harmony.");
        _traceEnabled = config.Bind("Trace", "Enabled", false,
            "Grava trace detalhado do PgTranslateLive em plugins/PgTranslateLive/trace.log.");
        _traceVerboseHooks = config.Bind("Trace", "VerboseHooks", false,
            "Se true, grava uma linha por chamada de hook. Pode aumentar IO; use só para teste curto.");
        _traceSnapshotIntervalSec = config.Bind("Trace", "SnapshotIntervalSec", 3,
            "Intervalo em segundos entre snapshots de contadores/durações no trace.log.");
        _selectionDebug = config.Bind("Diagnostic", "SelectionDebug", true,
            "Loga campos do UISelection no clique (etapa DebugSelecao) — separado da coleta Interacao.");
        _npcIdDebug = config.Bind("Diagnostic", "NpcIdDebug", true,
            "Loga npcId nos hooks de Talk (ProcessStartInteraction/PreTalk) — etapa DebugNpcId.");
        _subtextDebug = config.Bind("Diagnostic", "SubtextDebug", true,
            "Lista campos testados ao capturar subtexto — etapa DebugSubtexto (InteractionSubtextCapture). " +
            LogPolicy.Reminder);
        _translationDirOverride = config.Bind("Paths", "TranslationDir", "",
            "Pasta Translation/ única (strings_*.json). Vazio = Proton LocalLow app 342940.");
    }
}

#endregion

#region Paths
// --- GameDataPaths.cs ---
/// <summary>
/// Pasta Translation/ única — Proton LocalLow (app 342940) ou override em [Paths] TranslationDir.
/// Plugin: consulta json → Google → grava no mesmo caminho.
/// </summary>
internal static class GameDataPaths
{
    private const string GameFolder = "Elder Game";
    private const string ProjectFolder = "Project Gorgon";
    private const string TranslationFolder = "Translation";
    private const int SteamAppIdProjectGorgon = 342940;

    internal static string TranslationDir => ResolveTranslationDir();
    internal static string NpcTalkPath => Path.Combine(TranslationDir, NpcTalkStore.FileName);
    internal static string LegacyNpcYamlPath => Path.Combine(TranslationDir, NpcTalkStore.LegacyYamlName);

    private static string ResolveTranslationDir()
    {
        var configured = PluginSettings.TranslationDirOverride;
        if (!string.IsNullOrWhiteSpace(configured))
            return EnsureDir(Path.GetFullPath(configured));

        return EnsureDir(ResolveCanonicalTranslationDir());
    }

    private static string ResolveCanonicalTranslationDir()
    {
        // Proton/Wine: APPDATA → LocalLow (único runtime em Linux+Proton).
        var appData = Environment.GetEnvironmentVariable("APPDATA");
        if (!string.IsNullOrWhiteSpace(appData))
        {
            var localLow = Path.GetFullPath(Path.Combine(appData, "..", "LocalLow"));
            return Path.Combine(localLow, GameFolder, ProjectFolder, TranslationFolder);
        }

        // Linux host (sem Wine): prefix Proton 342940.
        var home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrWhiteSpace(home))
        {
            foreach (var steamRoot in CandidateSteamRoots(home))
            {
                var dir = ProtonTranslationDir(steamRoot, SteamAppIdProjectGorgon);
                var compat = Path.Combine(steamRoot, "steamapps", "compatdata", SteamAppIdProjectGorgon.ToString());
                if (Directory.Exists(compat) || Directory.Exists(Path.GetDirectoryName(dir)!))
                    return dir;
            }

            foreach (var steamRoot in CandidateSteamRoots(home))
                return ProtonTranslationDir(steamRoot, SteamAppIdProjectGorgon);
        }

        // Windows nativo.
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var localLow = Path.GetFullPath(Path.Combine(localAppData, "..", "LocalLow"));
            return Path.Combine(localLow, GameFolder, ProjectFolder, TranslationFolder);
        }

        return Path.Combine(
            home ?? ".",
            ".config",
            "unity3d",
            GameFolder,
            ProjectFolder,
            TranslationFolder);
    }

    private static IEnumerable<string> CandidateSteamRoots(string home)
    {
        yield return Path.Combine(home, ".steam", "debian-installation");
        yield return Path.Combine(home, ".steam", "steam");
        yield return Path.Combine(home, ".local", "share", "Steam");
        yield return Path.Combine(
            home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
    }

    private static string ProtonTranslationDir(string steamRoot, int appId) =>
        Path.Combine(
            steamRoot,
            "steamapps",
            "compatdata",
            appId.ToString(),
            "pfx",
            "drive_c",
            "users",
            "steamuser",
            "AppData",
            "LocalLow",
            GameFolder,
            ProjectFolder,
            TranslationFolder);

    private static string EnsureDir(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
        return path;
    }
}

#endregion

#region Logging
// --- LogAscii.cs ---
/// <summary>
/// BepInEx no Proton/Wine nao renderiza UTF-8 no LogOutput — normaliza para ASCII.
/// </summary>
internal static class LogAscii
{
    internal static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        var normalized = text
            .Replace('\u2014', '-')
            .Replace('\u2013', '-')
            .Replace('\u2192', '-')
            .Replace('\u2190', '<')
            .Replace('\u00D7', 'x')
            .Normalize(NormalizationForm.FormD);

        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            sb.Append(c <= 127 ? c : ToAscii(c));
        }

        return sb.ToString();
    }

    internal static void LogInfo(string message) =>
        Plugin.Log.LogInfo(Sanitize(message));

    internal static void LogWarning(string message) =>
        Plugin.Log.LogWarning(Sanitize(message));

    internal static void LogError(string message) =>
        Plugin.Log.LogError(Sanitize(message));

    private static char ToAscii(char c) => c switch
    {
        >= '\u00C0' and <= '\u00C6' => 'A',
        >= '\u00E0' and <= '\u00E6' => 'a',
        >= '\u00C8' and <= '\u00CB' => 'E',
        >= '\u00E8' and <= '\u00EB' => 'e',
        >= '\u00CC' and <= '\u00CF' => 'I',
        >= '\u00EC' and <= '\u00EF' => 'i',
        >= '\u00D2' and <= '\u00D6' or '\u00D8' => 'O',
        >= '\u00F2' and <= '\u00F6' or '\u00F8' => 'o',
        >= '\u00D9' and <= '\u00DC' => 'U',
        >= '\u00F9' and <= '\u00FC' => 'u',
        '\u00C7' or '\u00E7' => 'c',
        '\u00D1' => 'N',
        '\u00F1' => 'n',
        _ => '?',
    };
}

// --- TalkLog.cs ---
// LogPolicy.Reminder — etapas Interacao, DebugSubtexto, Consultar npc json, etc. sao protegidas.
internal enum LivePhase
{
    ShowTalk,
    DisplayTalkScreen,
    UpdateFancyMenu,
    UpdateFancyMenuOptions,
}

internal enum TalkPipelineStep
{
    Interacao,
    Falar,
    ConsultarNpcJson,
    Coletar,
    ConsultarJson,
    Google,
    AplicarTela,
}

internal static class TalkLog
{
    // LogPolicy.RulePath — nao remover metodos de log abaixo sem pedido explicito do usuario.
    internal const string FlowJson = "Falar > Coletar > Consultar json > Aplicar na tela";
    internal const string FlowGoogle = "Falar > Coletar > Consultar json > Google > Aplicar na tela";

    internal static bool Enabled => TranslateClient.LogVerbose;

    internal static void Info(string message)
    {
        if (!Enabled)
            return;
        LogAscii.LogInfo($"{Now()} {NpcPrefix()}{message}");
    }

    internal static void Warn(string message)
    {
        if (!Enabled)
            return;
        LogAscii.LogWarning($"{Now()} {NpcPrefix()}{message}");
    }

    internal static void FlowJsonPath()
    {
        if (!Enabled)
            return;
        Info($"fluxo: {FlowJson}");
    }

    internal static void FlowGooglePath()
    {
        if (!Enabled)
            return;
        Info($"fluxo: {FlowGoogle}");
    }

    internal static void Pipeline(TalkPipelineStep step, string detail = "")
    {
        if (step == TalkPipelineStep.Interacao)
        {
            Interacao(detail);
            return;
        }

        if (!Enabled)
            return;

        var label = step switch
        {
            TalkPipelineStep.Interacao => "Interacao",
            TalkPipelineStep.Falar => "Falar",
            TalkPipelineStep.ConsultarNpcJson => "Consultar npc json",
            TalkPipelineStep.Coletar => "Coletar",
            TalkPipelineStep.ConsultarJson => "Consultar json",
            TalkPipelineStep.Google => "Google",
            TalkPipelineStep.AplicarTela => "Aplicar na tela",
            _ => step.ToString(),
        };

        Info(string.IsNullOrWhiteSpace(detail)
            ? $"etapa {label}"
            : $"etapa {label}: {detail}");
    }

    /// <summary>Clique no NPC — sempre loga (independente de TalkVerbose). Ver <see cref="LogPolicy.RulePath"/>.</summary>
    internal static void Interacao(string detail = "")
    {
        var message = string.IsNullOrWhiteSpace(detail)
            ? "etapa Interacao"
            : $"etapa Interacao: {detail}";
        LogAscii.LogInfo($"{Now()} {NpcPrefix()}{message}");
    }

    /// <summary>Debug UISelection — separado da coleta Interacao (nao usa NpcPrefix).</summary>
    internal static void DebugSelecao(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return;

        LogAscii.LogInfo($"{Now()} etapa DebugSelecao: {detail}");
    }

    /// <summary>Debug npcId em hooks de Talk — separado da coleta Interacao.</summary>
    internal static void DebugNpcId(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return;

        LogAscii.LogInfo($"{Now()} etapa DebugNpcId: {detail}");
    }

    /// <summary>Subtexto capturado no clique — separado da etapa Interacao (sempre loga).</summary>
    internal static void Subtexto(string text, string source)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        LogAscii.LogInfo($"{Now()} {NpcPrefix()}etapa Subtexto ({source}): \"{Preview(text)}\"");
    }

    /// <summary>Probe de candidatos a subtexto — so dentro de InteractionSubtextCapture. Ver <see cref="LogPolicy.RulePath"/>.</summary>
    internal static void DebugSubtexto(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return;

        LogAscii.LogInfo($"{Now()} etapa DebugSubtexto: {detail}");
    }

    internal static void PhaseApplyOnly(LivePhase phase, IReadOnlyList<string> speeches, IReadOnlyList<string> choices)
    {
        if (!Enabled)
            return;

        switch (phase)
        {
            case LivePhase.DisplayTalkScreen:
                Pipeline(
                    TalkPipelineStep.AplicarTela,
                    TalkSession.GoogleUsedThisDialog
                        ? "traducao recente (Google nesta sessao)"
                        : "traducao em cache (sem Google)");
                break;
            case LivePhase.UpdateFancyMenuOptions:
                if (choices.Count > 0)
                {
                    Pipeline(
                        TalkPipelineStep.AplicarTela,
                        TalkSession.GoogleUsedThisDialog
                            ? "botoes recentes (Google nesta sessao)"
                            : "botoes em cache (sem Google)");
                }

                break;
        }
    }

    internal static void PhaseCollect(LivePhase phase, IReadOnlyList<string> speeches, IReadOnlyList<string> choices)
    {
        if (!Enabled)
            return;

        switch (phase)
        {
            case LivePhase.ShowTalk:
                Pipeline(TalkPipelineStep.Coletar, "ShowTalk");
                if (speeches.Count > 0)
                    Info($"Coleta da fala: {FormatList(speeches)}");
                if (choices.Count > 0)
                    Info($"Coleta dos botoes: {FormatList(choices)}");
                break;

            case LivePhase.DisplayTalkScreen:
                if (speeches.Count > 0)
                    Info($"Fala recebida: {FormatList(speeches)}");
                if (choices.Count > 0)
                    Info($"Botoes recebidos: {FormatList(choices)}");
                break;

            case LivePhase.UpdateFancyMenuOptions:
                if (choices.Count > 0)
                    Info($"Botoes do menu: {FormatList(choices)}");
                break;
        }
    }

    internal static void JsonResolved(TextKind kind, string source, string translated, string sourceFile)
    {
        if (!Enabled)
            return;

        var label = kind == TextKind.Choice ? "botao" : "fala";
        Pipeline(
            TalkPipelineStep.ConsultarJson,
            $"{sourceFile} ({label}) \"{Preview(source)}\" -> \"{Preview(translated)}\"");
    }

    internal static void JsonSessionHit(TextKind kind, string source, string translated)
    {
        if (!Enabled)
            return;

        var label = kind == TextKind.Choice ? "botao" : "fala";
        Pipeline(
            TalkPipelineStep.ConsultarJson,
            $"cache sessao ({label}) \"{Preview(source)}\" -> \"{Preview(translated)}\"");
    }

    internal static void JsonNotFound(TextKind kind, string source)
    {
        if (!Enabled)
            return;

        var label = kind == TextKind.Choice ? "botao" : "fala";
        Pipeline(TalkPipelineStep.ConsultarJson, $"nao encontrado ({label}) \"{Preview(source)}\"");
    }

    internal static void GoogleBatch(
        IReadOnlyList<string> speechPending,
        IReadOnlyList<string> choicePending)
    {
        if (!Enabled)
            return;

        FlowGooglePath();
        TalkSession.MarkGoogleUsed();

        var parts = new List<string>();
        if (speechPending.Count > 0)
            parts.Add($"fala {FormatList(speechPending)}");
        if (choicePending.Count > 0)
            parts.Add($"botoes {FormatList(choicePending)}");

        if (parts.Count > 0)
            Pipeline(TalkPipelineStep.Google, $"consultando {string.Join(" | ", parts)}");
    }

    internal static void GoogleTranslated(TextKind kind, string source, string translated)
    {
        if (!Enabled)
            return;

        var label = kind == TextKind.Choice ? "Botao" : "Fala";
        Pipeline(
            TalkPipelineStep.Google,
            $"{label} traduzida: \"{Preview(source)}\" -> \"{Preview(translated)}\"");
    }

    internal static void GoogleSkipped(TextKind kind, string text, string reason)
    {
        if (!Enabled)
            return;

        var label = kind == TextKind.Choice ? "Botao" : "Fala";
        Pipeline(TalkPipelineStep.Google, $"{label} sem traducao ({reason}): \"{Preview(text)}\"");
    }

    internal static void ShowingTranslated(string translatedSpeech)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(translatedSpeech))
            return;

        Pipeline(
            TalkPipelineStep.AplicarTela,
            $"dialogo na tela: \"{Preview(translatedSpeech, dialogue: true)}\"");
    }

    internal static void BatchCompleteJsonOnly(int jsonHits)
    {
        if (!Enabled || jsonHits <= 0)
            return;

        FlowJsonPath();
    }

    /// <summary>Consulta strings_npcs.json — log protegido. Ver <see cref="LogPolicy.RulePath"/>.</summary>
    internal static void NpcJsonChecking()
    {
        LogAscii.LogInfo($"{Now()} {NpcPrefix()}etapa Consultar npc json: consultando npc no json");
    }

    /// <summary>NPC encontrado no json — log protegido. Ver <see cref="LogPolicy.RulePath"/>.</summary>
    internal static void NpcJsonFound(string name, string subtext, string sourceFile)
    {
        var desc = string.IsNullOrWhiteSpace(subtext) ? "sem subtexto" : $"subtexto: \"{Preview(subtext)}\"";
        LogAscii.LogInfo($"{Now()} {NpcPrefix()}npc ja existe no json ({sourceFile}): {name} | {desc}");
    }

    /// <summary>NPC ausente no json — log protegido. Ver <see cref="LogPolicy.RulePath"/>.</summary>
    internal static void NpcJsonMissing(string name, string subtext)
    {
        if (string.IsNullOrWhiteSpace(subtext))
            LogAscii.LogInfo($"{Now()} {NpcPrefix()}nao tem no json: {name} | salvando");
        else
            LogAscii.LogInfo($"{Now()} {NpcPrefix()}nao tem no json: {name} | salvando com subtexto: \"{Preview(subtext)}\"");
    }

    internal static void SubtextResolved(string translated, string sourceFile)
    {
        if (!Enabled)
            return;

        Pipeline(
            TalkPipelineStep.ConsultarJson,
            $"{sourceFile} (subtexto) -> \"{Preview(translated)}\"");
    }

    internal static void SubtextTranslated(string source, string translated)
    {
        if (!Enabled)
            return;

        FlowGooglePath();
        Pipeline(
            TalkPipelineStep.Google,
            $"subtexto traduzido: \"{Preview(source)}\" -> \"{Preview(translated)}\"");
    }

    internal static void SubtextApplied(string source, string translated)
    {
        if (!Enabled)
            return;

        Pipeline(
            TalkPipelineStep.AplicarTela,
            $"subtexto na tela: \"{Preview(translated)}\"");
    }

    internal static void NpcJsonSaved(string name, string npcId, string fileName)
    {
        LogAscii.LogInfo($"{Now()} {NpcPrefix()}salvou npc {name} ({npcId}) em {fileName}");
    }

    internal static string Preview(string s, bool dialogue = false) =>
        string.IsNullOrEmpty(s)
            ? "(vazio)"
            : dialogue && s.Length > 120
                ? s[..117] + "..."
                : s.Length <= 80
                    ? s
                    : s[..77] + "...";

    private static string NpcPrefix()
    {
        if (string.IsNullOrEmpty(TalkSession.NpcName))
            return "";
        return $"[{TalkSession.NpcName}] ";
    }

    private static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    private static string FormatList(IEnumerable<string> items) =>
        string.Join(", ", items.Select(s => $"\"{Preview(s)}\""));
}

// --- TraceLog.cs ---
/// <summary>
/// Trace próprio em arquivo, fora do pipeline de log do BepInEx.
/// </summary>
internal static class TraceLog
{
    private static readonly ConcurrentQueue<string> Queue = new();
    private static readonly ConcurrentDictionary<string, long> Counters = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, long> MaxTicks = new(StringComparer.Ordinal);
    private static readonly AutoResetEvent Signal = new(false);

    private static CancellationTokenSource? _cts;
    private static Task? _writerTask;
    private static Task? _snapshotTask;
    private static string _path = "";

    internal static bool Enabled => PluginSettings.TraceEnabled;
    internal static bool VerboseHooks => PluginSettings.TraceVerboseHooks;

    internal static void Start()
    {
        if (!Enabled || _cts != null)
            return;

        var dir = Path.Combine(Paths.BepInExRootPath, "plugins", "PgTranslateLive");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "trace.log");
        _cts = new CancellationTokenSource();

        EnqueueRaw("");
        EnqueueRaw($"===== PgTranslateLive trace start {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} =====");
        EnqueueRaw($"strategy={PluginSettings.Strategy} diagnostic={PluginSettings.DiagnosticMode} verboseHooks={VerboseHooks}");

        _writerTask = Task.Run(() => WriterLoop(_cts.Token));
        _snapshotTask = Task.Run(() => SnapshotLoop(_cts.Token));
    }

    internal static void Stop()
    {
        var cts = _cts;
        if (cts == null)
            return;

        Event("trace.stop");
        cts.Cancel();
        Signal.Set();
    }

    internal static IDisposable Scope(string name)
    {
        if (!Enabled)
            return NoopScope.Instance;

        Counter($"{name}.calls");
        Event($"{name}.begin");
        return new TimedScope(name);
    }

    internal static void Event(string message)
    {
        if (!Enabled)
            return;

        EnqueueRaw($"{DateTime.Now:HH:mm:ss.fff} T{Environment.CurrentManagedThreadId:D2} {message}");
    }

    internal static void Error(string message, Exception ex)
    {
        if (!Enabled)
            return;

        EnqueueRaw($"{DateTime.Now:HH:mm:ss.fff} T{Environment.CurrentManagedThreadId:D2} ERROR {message}: {ex}");
    }

    internal static void Counter(string name, long delta = 1)
    {
        if (!Enabled)
            return;

        Counters.AddOrUpdate(name, delta, (_, current) => current + delta);
    }

    internal static void Hook(string name)
    {
        Counter($"hook.{name}");
        if (VerboseHooks)
            Event($"hook.{name}");
    }

    internal static void Duration(string name, Stopwatch sw)
    {
        if (!Enabled)
            return;

        var ticks = sw.ElapsedTicks;
        MaxTicks.AddOrUpdate(name, ticks, (_, current) => Math.Max(current, ticks));
        Event($"{name}.end {sw.Elapsed.TotalMilliseconds:F3}ms");
    }

    private static void EnqueueRaw(string line)
    {
        Queue.Enqueue(line);
        Signal.Set();
    }

    private static void WriterLoop(CancellationToken token)
    {
        try
        {
            using var writer = new StreamWriter(new FileStream(
                _path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite))
            {
                AutoFlush = false,
            };

            while (!token.IsCancellationRequested || !Queue.IsEmpty)
            {
                Signal.WaitOne(TimeSpan.FromSeconds(1));

                var wrote = false;
                while (Queue.TryDequeue(out var line))
                {
                    writer.WriteLine(line);
                    wrote = true;
                }

                if (wrote)
                    writer.Flush();
            }
        }
        catch (Exception ex)
        {
            LogAscii.LogWarning($"TraceLog writer falhou: {ex.Message}");
        }
    }

    private static async Task SnapshotLoop(CancellationToken token)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, PluginSettings.TraceSnapshotIntervalSec));
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, token).ConfigureAwait(false);
                Snapshot();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Error("trace.snapshot", ex);
            }
        }
    }

    private static void Snapshot()
    {
        if (!Enabled)
            return;

        EnqueueRaw($"--- snapshot {DateTime.Now:HH:mm:ss.fff} ---");

        foreach (var kv in Counters)
            EnqueueRaw($"counter {kv.Key}={kv.Value}");

        foreach (var kv in MaxTicks)
        {
            var ms = kv.Value * 1000.0 / Stopwatch.Frequency;
            EnqueueRaw($"max {kv.Key}={ms:F3}ms");
        }
    }

    private sealed class TimedScope : IDisposable
    {
        private readonly string _name;
        private readonly Stopwatch _sw;

        internal TimedScope(string name)
        {
            _name = name;
            _sw = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            _sw.Stop();
            Duration(_name, _sw);
        }
    }

    private sealed class NoopScope : IDisposable
    {
        internal static readonly NoopScope Instance = new();
        public void Dispose()
        {
        }
    }
}

#endregion

#region Il2Cpp
// --- Il2CppArrayReflection.cs ---
/// <summary>
/// Reflection de arrays Il2Cpp cacheada no boot — evita lookup a cada ShowTalk.
/// </summary>
internal static class Il2CppArrayReflection
{
    private static PropertyInfo? _length;
    private static MethodInfo? _getItem;
    private static MethodInfo? _setItem;

    internal static void EnsureInitialized(object arraySample)
    {
        if (_getItem != null)
            return;

        var type = arraySample.GetType();
        _length = type.GetProperty("Length", BindingFlags.Public | BindingFlags.Instance);
        _getItem = type.GetMethod("get_Item", BindingFlags.Public | BindingFlags.Instance);
        _setItem = type.GetMethod("set_Item", BindingFlags.Public | BindingFlags.Instance);
    }

    internal static int GetLength(object array)
    {
        EnsureInitialized(array);
        return _length != null ? (int)_length.GetValue(array)! : 0;
    }

    internal static string? GetStringAt(object array, int index)
    {
        EnsureInitialized(array);
        if (_getItem == null)
            return null;

        return Il2CppStringHelper.Read(_getItem.Invoke(array, new object[] { index }));
    }

    internal static object? GetObjectAt(object array, int index)
    {
        EnsureInitialized(array);
        return _getItem?.Invoke(array, new object[] { index });
    }

    internal static void SetStringAt(object array, int index, string value)
    {
        EnsureInitialized(array);
        _setItem?.Invoke(array, new object[] { index, Il2CppStringHelper.Write(value) });
    }
}

// --- Il2CppStringHelper.cs ---
internal static class Il2CppStringHelper
{
    internal static string Read(object value)
    {
        if (value == null)
            return null;

        if (value is string managed)
            return managed;

        // Il2CppSystem.String and other wrappers expose text via ToString().
        return value.ToString();
    }

    /// <summary>
    /// Il2CppInterop marshals managed strings into Il2Cpp at invoke time.
    /// </summary>
    internal static object Write(string value) => value;

    internal static bool IsStringType(Type type) =>
        type == typeof(string) || type.FullName == "Il2CppSystem.String";
}

#endregion

#region Stores
// --- CdnStringStore.cs ---
/// <summary>
/// Fallback: consulta strings_*.json da pasta Translation/ (itens, npcs, ui…)
/// cruzando EN (CDN oficial) com PT (language pack instalado).
/// </summary>
internal static class CdnStringStore
{
    private const string CdnBase = "https://cdn.projectgorgon.com";
    private const string EnCacheFolder = ".pg-en-cache";
    private const int MinEmbedWordLength = 4;

    private static readonly object Lock = new();
    private static readonly string[] FallbackFiles =
    {
        "strings_items.json",
        "strings_npcs.json",
        "strings_ui.json",
        "strings_requested.json",
        "strings_skills.json",
        "strings_abilities.json",
        "strings_quests.json",
        "strings_recipes.json",
    };

    private static Dictionary<string, string> _bySource = new(StringComparer.Ordinal);
    private static Dictionary<string, string> _bySourceIgnoreCase = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, string> _sourceFileBySource = new(StringComparer.Ordinal);
    private static List<string> _embedWordKeys = new();
    private static bool _loaded;

    private static ConfigEntry<bool> _useLookup = null!;

    internal static bool UseLookup => _useLookup?.Value ?? true;
    internal static int Count
    {
        get
        {
            lock (Lock)
                return _bySource.Count;
        }
    }

    internal static void Init(ConfigFile config)
    {
        using var scope = TraceLog.Scope("CdnStringStore.Init.inner");
        _useLookup = config.Bind("NpcTalk", "UseCdnFallback", true,
            "Consulta outros strings_*.json em Translation/ (itens, npcs, ui…) antes do Google.");
        TraceLog.Event($"CdnStringStore.Init useLookup={UseLookup}");

        if (UseLookup)
            EnsureLoaded();
    }

    internal static void Reload()
    {
        lock (Lock)
        {
            _loaded = false;
            _bySource = new Dictionary<string, string>(StringComparer.Ordinal);
            _bySourceIgnoreCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _sourceFileBySource = new Dictionary<string, string>(StringComparer.Ordinal);
            _embedWordKeys = new List<string>();
        }

        EnsureLoaded();
    }

    internal static bool TryGet(string source, out string translated, out string sourceFile) =>
        TryTranslate(source, out translated, out sourceFile, embed: false);

    /// <summary>Exact, case-insensitive exact, then word/phrase embed (ex.: "Maintain Lamp").</summary>
    internal static bool TryTranslate(string source, out string translated, out string sourceFile, bool embed = true)
    {
        TraceLog.Counter("CdnStringStore.TryGet");
        translated = "";
        sourceFile = "";
        if (!UseLookup || string.IsNullOrWhiteSpace(source))
        {
            TraceLog.Counter("CdnStringStore.TryGet.skip");
            return false;
        }

        EnsureLoaded();

        lock (Lock)
        {
            if (_bySource.TryGetValue(source, out translated!))
            {
                _sourceFileBySource.TryGetValue(source, out sourceFile!);
                TraceLog.Counter("CdnStringStore.TryGet.hit");
                return true;
            }

            if (_bySourceIgnoreCase.TryGetValue(source, out translated!))
            {
                _sourceFileBySource.TryGetValue(source, out sourceFile!);
                TraceLog.Counter("CdnStringStore.TryGet.hitIgnoreCase");
                return true;
            }

            if (!embed || source.Length > 512)
            {
                TraceLog.Counter("CdnStringStore.TryGet.miss");
                return false;
            }

            if (TryReplaceEmbedded(source, out translated, out sourceFile))
            {
                TraceLog.Counter("CdnStringStore.TryGet.hitEmbed");
                return true;
            }

            TraceLog.Counter("CdnStringStore.TryGet.miss");
            return false;
        }
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
            return;

        lock (Lock)
        {
            if (_loaded)
                return;

            using var scope = TraceLog.Scope("CdnStringStore.EnsureLoaded");
            var translationDir = GameDataPaths.TranslationDir;
            var cdnVersion = ReadCdnFileVersion(translationDir);
            var added = 0;

            foreach (var fileName in FallbackFiles)
            {
                var ptPath = Path.Combine(translationDir, fileName);
                if (!File.Exists(ptPath))
                {
                    TraceLog.Event($"CdnStringStore.skip missing pt={fileName}");
                    continue;
                }

                var ptMap = LoadJsonMap(ptPath);
                if (ptMap.Count == 0)
                    continue;

                var enMap = LoadEnglishMap(translationDir, cdnVersion, fileName);
                if (enMap.Count == 0)
                {
                    TraceLog.Event($"CdnStringStore.skip noEnMap file={fileName}");
                    continue;
                }

                var fileAdded = MergeMaps(fileName, enMap, ptMap);
                added += fileAdded;
                TraceLog.Event($"CdnStringStore.file file={fileName} pt={ptMap.Count} en={enMap.Count} added={fileAdded}");
            }

            _embedWordKeys = _bySource.Keys
                .Where(k => k.Length >= MinEmbedWordLength && !k.Contains('\n'))
                .OrderByDescending(k => k.Length)
                .ToList();

            _loaded = true;
            LogAscii.LogInfo(
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} CdnStringStore: {_bySource.Count} entradas ({added} novas) | {translationDir}");
        }
    }

    private static bool TryReplaceEmbedded(string source, out string translated, out string sourceFile)
    {
        translated = source;
        sourceFile = "";
        var changed = false;

        foreach (var en in _embedWordKeys)
        {
            if (!_bySource.TryGetValue(en, out var pt))
                continue;

            if (string.Equals(en, pt, StringComparison.Ordinal))
                continue;

            var idx = 0;
            while ((idx = translated.IndexOf(en, idx, StringComparison.Ordinal)) >= 0)
            {
                if (!HasWordBoundary(translated, idx, en.Length))
                {
                    idx++;
                    continue;
                }

                translated = translated[..idx] + pt + translated[(idx + en.Length)..];
                sourceFile = _sourceFileBySource.TryGetValue(en, out var f) ? f : "";
                changed = true;
                idx += pt.Length;
            }
        }

        return changed;
    }

    private static bool HasWordBoundary(string text, int index, int length)
    {
        if (index > 0)
        {
            var before = text[index - 1];
            if (char.IsLetterOrDigit(before) || before == '_')
                return false;
        }

        var end = index + length;
        if (end < text.Length)
        {
            var after = text[end];
            if (char.IsLetterOrDigit(after) || after == '_')
                return false;
        }

        return true;
    }

    private static int MergeMaps(string fileName, Dictionary<string, string> enMap, Dictionary<string, string> ptMap)
    {
        var added = 0;
        foreach (var (key, ptVal) in ptMap)
        {
            if (string.IsNullOrWhiteSpace(ptVal))
                continue;

            if (!enMap.TryGetValue(key, out var enVal) || string.IsNullOrWhiteSpace(enVal))
                continue;

            if (string.Equals(enVal, ptVal, StringComparison.Ordinal))
                continue;

            if (_bySource.ContainsKey(enVal))
                continue;

            _bySource[enVal] = ptVal;
            _sourceFileBySource[enVal] = fileName;
            if (!_bySourceIgnoreCase.ContainsKey(enVal))
                _bySourceIgnoreCase[enVal] = ptVal;
            added++;
        }

        return added;
    }

    private static string ReadCdnFileVersion(string translationDir)
    {
        var versionPath = Path.Combine(translationDir, "version.json");
        if (!File.Exists(versionPath))
        {
            TraceLog.Event("CdnStringStore.ReadCdnFileVersion missing version.json");
            return "";
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(versionPath, Encoding.UTF8));
            if (doc.RootElement.TryGetProperty("CdnFileVersion", out var prop))
                return prop.GetString()?.Trim() ?? "";
        }
        catch (Exception ex)
        {
            TraceLog.Error("CdnStringStore.ReadCdnFileVersion", ex);
            TalkLog.Warn($"Erro ao ler version.json: {ex.Message}");
        }

        return "";
    }

    private static Dictionary<string, string> LoadEnglishMap(string translationDir, string cdnVersion, string fileName)
    {
        if (string.IsNullOrWhiteSpace(cdnVersion))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var cachePath = Path.Combine(translationDir, EnCacheFolder, cdnVersion, fileName);
        if (File.Exists(cachePath))
            return LoadJsonMap(cachePath);

        var url = $"{CdnBase}/v{cdnVersion}/data/Translation/{fileName}";
        try
        {
            using var response = TranslateClient.SharedHttp.GetAsync(url).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                TraceLog.Event($"CdnStringStore.LoadEnglishMap http={(int)response.StatusCode} file={fileName}");
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var dir = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(cachePath, json, Encoding.UTF8);
            TraceLog.Event($"CdnStringStore.LoadEnglishMap cached file={fileName}");
            return ParseJsonMap(json);
        }
        catch (Exception ex)
        {
            TraceLog.Error($"CdnStringStore.LoadEnglishMap file={fileName}", ex);
            TalkLog.Warn($"CDN EN indisponível ({fileName}): {ex.Message}");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static Dictionary<string, string> LoadJsonMap(string path)
    {
        try
        {
            return ParseJsonMap(File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true)));
        }
        catch (Exception ex)
        {
            TraceLog.Error($"CdnStringStore.LoadJsonMap path={path}", ex);
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static Dictionary<string, string> ParseJsonMap(string json)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        if (data == null)
            return entries;

        foreach (var kv in data)
        {
            if (!string.IsNullOrEmpty(kv.Key))
                entries[kv.Key] = kv.Value ?? "";
        }

        return entries;
    }
}

// --- NpcRegistryStore.cs ---
/// <summary>
/// Consulta strings_npcs.json / strings_requested.json e acrescenta NPCs novos em strings_npcs.json.
/// </summary>
internal static class NpcRegistryStore
{
    internal const string NpcsFileName = "strings_npcs.json";
    internal const string LegacyRegistryFileName = "strings_npcregistry.json";
    internal const string FileName = NpcsFileName;

    private static readonly object Lock = new();
    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static ConfigEntry<bool> _saveNew = null!;
    private static string _path = "";

    internal static bool SaveNew => _saveNew?.Value ?? true;

    internal static void Init(ConfigFile config)
    {
        _saveNew = config.Bind("NpcTalk", "SaveNewNpcRegistry", true,
            "Acrescenta NPCs descobertos (nome + subtexto) em Translation/strings_npcs.json.");
        _path = Path.Combine(GameDataPaths.TranslationDir, NpcsFileName);
        TraceLog.Event($"NpcRegistryStore.Init path={_path} saveNew={SaveNew}");
    }

    internal static void EnsureOnTalk(string npcId, string displayName, string subtext)
    {
        if (string.IsNullOrWhiteSpace(npcId))
            return;

        TalkLog.NpcJsonChecking();

        var name = string.IsNullOrWhiteSpace(displayName)
            ? NpcNameResolver.Resolve(npcId)
            : displayName.Trim();
        var desc = subtext?.Trim() ?? "";

        if (TryFindInPack(npcId, out var found))
        {
            var logName = string.IsNullOrWhiteSpace(found.Name) ? name : found.Name;
            var logDesc = string.IsNullOrWhiteSpace(found.Desc) ? desc : found.Desc;
            TalkLog.NpcJsonFound(logName, logDesc, found.SourceFile);
            return;
        }

        TalkLog.NpcJsonMissing(name, desc);

        if (!SaveNew)
            return;

        if (TrySaveIfMissing(ResolveNpcId(npcId, name), name, desc))
        {
            NpcNameResolver.Invalidate();
            TalkLog.NpcJsonSaved(name, ResolveNpcId(npcId, name), FileName);
        }
    }

    /// <summary>Etapa Interacao — salva no registry mesmo sem subtexto.</summary>
    internal static bool SaveOnInteraction(string npcId, string displayName, string subtext)
    {
        if (!SaveNew)
            return false;

        var name = string.IsNullOrWhiteSpace(displayName)
            ? NpcNameResolver.Resolve(npcId)
            : displayName.Trim();
        var id = ResolveNpcId(npcId, name);
        if (string.IsNullOrWhiteSpace(id))
            return false;

        return TrySaveIfMissing(id, name, subtext?.Trim() ?? "");
    }

    internal static string ResolveNpcId(string npcId, string displayName)
    {
        if (!string.IsNullOrWhiteSpace(npcId))
            return npcId.Trim();

        if (!string.IsNullOrWhiteSpace(displayName)
            && NpcNameResolver.TryResolveId(displayName.Trim(), out var resolved))
            return resolved;

        return GuessNpcIdFromName(displayName);
    }

    private static string GuessNpcIdFromName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        var sanitized = name.Trim().Replace(' ', '_');
        return sanitized.StartsWith("NPC_", StringComparison.Ordinal)
            ? sanitized
            : $"NPC_{sanitized}";
    }

    private static bool TrySaveIfMissing(string npcId, string name, string desc)
    {
        if (string.IsNullOrWhiteSpace(npcId))
            return false;

        if (TryFindInPack(npcId, out _))
            return false;

        return TryAddToRegistry(npcId, name, desc);
    }

    /// <summary>
    /// Traduz subtexto do NPC: pack JSON → npctalk/cdn → Google. Grava EN no registry se novo.
    /// </summary>
    internal static string ResolveSubtext(string npcId, string descEn)
    {
        if (string.IsNullOrWhiteSpace(descEn))
            return descEn;

        descEn = descEn.Trim();

        if (TryFindInPack(npcId, out var found) && !string.IsNullOrWhiteSpace(found.Desc))
        {
            TalkLog.SubtextResolved(found.Desc, found.SourceFile);
            return found.Desc;
        }

        if (CdnStringStore.TryGet(descEn, out var cached, out var cdnSource))
        {
            TalkLog.SubtextResolved(cached, cdnSource);
            return cached;
        }

        if (!TranslateClient.Enabled)
            return descEn;

        var translated = TranslateClient.TryTranslateBatch(
            new[] { descEn },
            new[] { TextKind.General });

        if (translated.Length == 0 || string.IsNullOrWhiteSpace(translated[0]) || translated[0] == descEn)
            return descEn;

        // Subtexto vai para strings_npcs.json (registry), nunca para strings_npctalk.json.
        TalkLog.SubtextTranslated(descEn, translated[0]);
        return translated[0];
    }

    internal static bool TryGetNpcContext(string npcId, out string name, out string desc, out string sourceFile)
    {
        name = "";
        desc = "";
        sourceFile = "";

        if (!TryFindInPack(npcId, out var found))
            return false;

        name = found.Name;
        desc = found.Desc;
        sourceFile = found.SourceFile;
        return true;
    }

    private static bool TryFindInPack(string npcId, out NpcJsonEntry entry)
    {
        entry = default;
        var dir = GameDataPaths.TranslationDir;
        var files = new[]
        {
            (NpcsFileName, true),
            ("strings_requested.json", false),
            (LegacyRegistryFileName, true),
        };

        foreach (var (fileName, npcInfoFormat) in files)
        {
            var path = Path.Combine(dir, fileName);
            if (!File.Exists(path))
                continue;

            if (TryFindInFile(path, npcId, npcInfoFormat, out var name, out var desc))
            {
                entry = new NpcJsonEntry(name ?? "", desc ?? "", fileName);
                return true;
            }
        }

        return false;
    }

    private static bool TryFindInFile(
        string path,
        string npcId,
        bool npcInfoFormat,
        out string? name,
        out string? desc)
    {
        name = null;
        desc = null;
        Dictionary<string, string>? map;
        try
        {
            map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception ex)
        {
            TraceLog.Error($"NpcRegistryStore.TryFindInFile path={path}", ex);
            return false;
        }

        if (map == null || map.Count == 0)
            return false;

        if (npcInfoFormat)
        {
            map.TryGetValue($"npcinfo_{npcId}_Name", out name);
            map.TryGetValue($"npcinfo_{npcId}_Desc", out desc);
        }
        else
        {
            foreach (var key in map.Keys)
            {
                if (key.EndsWith($"/{npcId}_Name", StringComparison.Ordinal))
                    name = map[key];
                else if (key.EndsWith($"/{npcId}_Desc", StringComparison.Ordinal))
                    desc = map[key];
            }
        }

        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(desc))
            return false;

        return true;
    }

    private static bool TryAddToRegistry(string npcId, string name, string desc)
    {
        lock (Lock)
        {
            var entries = LoadRegistry(_path);

            var nameKey = $"npcinfo_{npcId}_Name";
            var descKey = $"npcinfo_{npcId}_Desc";
            if (entries.ContainsKey(nameKey))
                return false;

            entries[nameKey] = name;
            entries[descKey] = desc;
            WriteRegistry(_path, entries);
            TraceLog.Event($"NpcRegistryStore.added id={npcId} name={name} desc={TalkLog.Preview(desc)}");
            return true;
        }
    }

    private static Dictionary<string, string> LoadRegistry(string path)
    {
        if (!File.Exists(path))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path, Encoding.UTF8))
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            TraceLog.Error("NpcRegistryStore.LoadRegistry", ex);
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static void WriteRegistry(string path, Dictionary<string, string> entries)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var sorted = entries
            .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);

        var payload = JsonSerializer.Serialize(sorted, JsonWriteOptions);
        File.WriteAllText(path, payload + Environment.NewLine, Encoding.UTF8);
    }

    internal readonly struct NpcJsonEntry
    {
        internal NpcJsonEntry(string name, string desc, string sourceFile)
        {
            Name = name;
            Desc = desc;
            SourceFile = sourceFile;
        }

        internal string Name { get; }
        internal string Desc { get; }
        internal string SourceFile { get; }
    }
}

// --- NpcTalkStore.cs ---
/// <summary>
/// Carrega/salva Translation/strings_npctalk.json (falas Falar — cache Google).
/// </summary>
internal static class NpcTalkStore
{
    internal const string FileName = "strings_npctalk.json";
    internal const string LegacyYamlName = "npcs.yaml";

    private static readonly object Lock = new();
    private static Dictionary<string, string> _entries = new(StringComparer.Ordinal);
    private static string _path = "";

    private static ConfigEntry<bool> _useLookup = null!;
    private static ConfigEntry<bool> _saveNew = null!;

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly Regex CommunityScoreEn = new(
        @"Community Score:\s*(\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CommunityScorePt = new(
        @"Pontuação da comunidade:\s*[\d.,]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const string WritStatsMarker = "Current Writ Challenge Statistics for Laeth";

    internal static bool UseLookup => _useLookup?.Value ?? false;
    internal static int Count
    {
        get
        {
            lock (Lock)
                return _entries.Count;
        }
    }

    internal static void Init(ConfigFile config)
    {
        using var scope = TraceLog.Scope("NpcTalkStore.Init.inner");
        _useLookup = config.Bind("NpcTalk", "UseLookup", true,
            "Consulta Translation/strings_npctalk.json antes do Google Translate.");
        _saveNew = config.Bind("NpcTalk", "SaveNewTranslations", true,
            "Salva falas novas em Translation/strings_npctalk.json após traduzir via Google.");
        _path = ResolvePath();
        TraceLog.Event($"NpcTalkStore.Init path={_path} useLookup={UseLookup} saveNew={_saveNew.Value}");

        if (UseLookup || _saveNew.Value)
            Reload();
        else
            LogAscii.LogInfo(
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} NpcTalk: lookup/save desligados | {_path}");
    }

    internal static void Reload()
    {
        using var scope = TraceLog.Scope("NpcTalkStore.Reload");
        _path = ResolvePath();
        MaybeMigrateLegacyYaml();

        lock (Lock)
            _entries = LoadJsonFile(_path);

        TraceLog.Event($"NpcTalkStore.Reload entries={_entries.Count} path={_path}");
        LogAscii.LogInfo(
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} NpcTalk: {_entries.Count} entradas | {_path}");
    }

    internal static bool TryGet(string source, out string translated) =>
        TryGet(source, out translated, out _);

    internal static bool TryGet(string source, out string translated, out string matchMode)
    {
        TraceLog.Counter("NpcTalkStore.TryGet");
        translated = "";
        matchMode = "";
        if (!UseLookup || string.IsNullOrWhiteSpace(source))
        {
            TraceLog.Counter("NpcTalkStore.TryGet.skip");
            return false;
        }

        var key = NormalizeKey(source);
        lock (Lock)
        {
            if (_entries.TryGetValue(key, out translated!))
            {
                matchMode = "exact";
                TraceLog.Counter("NpcTalkStore.TryGet.hit");
                return true;
            }

            if (!string.Equals(key, source, StringComparison.Ordinal)
                && _entries.TryGetValue(source, out translated!))
            {
                matchMode = "exact";
                TraceLog.Counter("NpcTalkStore.TryGet.hit");
                return true;
            }

            if (TryGetWritStatsTemplate(key, out translated))
            {
                matchMode = "template";
                TraceLog.Counter("NpcTalkStore.TryGet.hitTemplate");
                TraceLog.Event($"NpcTalkStore.TryGet.writTemplate score={CommunityScoreEn.Match(key).Groups[1].Value}");
                return true;
            }
        }

        TraceLog.Counter("NpcTalkStore.TryGet.miss");
        return false;
    }

    /// <summary>Só falas ShowTalk/Falar — rejeita subtexto, prefs de vendor e lixo legado npcs.yaml.</summary>
    internal static bool IsEligibleTalkEntry(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        source = source.Trim();

        if (!source.Contains(' ', StringComparison.Ordinal))
            return false;

        if (source.StartsWith("x:", StringComparison.OrdinalIgnoreCase)
            && source.Contains(" y:", StringComparison.OrdinalIgnoreCase))
            return false;

        if (CommunityScoreEn.IsMatch(source)
            || source.Contains("Friends:", StringComparison.Ordinal)
            || source.StartsWith("BestFriends", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("CloseFriends", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("Despised:", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("LikeFamily:", StringComparison.OrdinalIgnoreCase))
            return false;

        if (IsNpcSubtextOrVendorBlurb(source))
            return false;

        return IsLikelyTalkLine(source);
    }

    private static bool ContainsYouAddress(string source) =>
        source.Contains(" you ", StringComparison.OrdinalIgnoreCase)
        || source.StartsWith("You ", StringComparison.OrdinalIgnoreCase)
        || source.Contains(" you.", StringComparison.OrdinalIgnoreCase)
        || source.Contains(" you,", StringComparison.OrdinalIgnoreCase)
        || source.Contains(" you?", StringComparison.OrdinalIgnoreCase)
        || source.Contains(" you!", StringComparison.OrdinalIgnoreCase);

    private static bool IsNpcSubtextOrVendorBlurb(string source)
    {
        if (ContainsYouAddress(source))
            return false;

        var lower = source.ToLowerInvariant();

        if (source.StartsWith("A ", StringComparison.Ordinal)
            || source.StartsWith("An ", StringComparison.Ordinal))
        {
            if (lower.Contains(" selling ")
                || lower.Contains(" vendor")
                || lower.Contains(" demeanor")
                || lower.Contains(" looks ")
                || lower.Contains(" with a ")
                || lower.Contains(" covered in ")
                || lower.Contains(" attempting to ")
                || lower.Contains(" constantly ")
                || (source.EndsWith('.') && source.Length < 120))
                return true;
        }

        if ((source.StartsWith("He ", StringComparison.Ordinal)
             || source.StartsWith("She ", StringComparison.Ordinal)
             || source.StartsWith("It ", StringComparison.Ordinal)
             || source.StartsWith("He's ", StringComparison.OrdinalIgnoreCase)
             || source.StartsWith("She's ", StringComparison.OrdinalIgnoreCase)
             || source.StartsWith("His ", StringComparison.Ordinal)
             || source.StartsWith("Her ", StringComparison.Ordinal))
            && !ContainsYouAddress(source))
            return true;

        if (source.StartsWith("Looking ", StringComparison.Ordinal)
            || source.StartsWith("Busy ", StringComparison.Ordinal)
            || source.StartsWith("Laughing ", StringComparison.Ordinal)
            || source.StartsWith("Checking ", StringComparison.Ordinal)
            || source.StartsWith("Fiddling ", StringComparison.Ordinal)
            || source.StartsWith("Distractedly ", StringComparison.Ordinal))
            return true;

        return false;
    }

    private static bool IsLikelyTalkLine(string source)
    {
        if (ContainsYouAddress(source))
            return true;

        if (source.StartsWith('"') || source.StartsWith('\''))
            return true;

        if (source.Contains('?') && source.Length >= 12
            && !source.StartsWith("Is he ", StringComparison.OrdinalIgnoreCase)
            && !source.StartsWith("Is she ", StringComparison.OrdinalIgnoreCase)
            && !source.StartsWith("Is it ", StringComparison.OrdinalIgnoreCase)
            && !source.StartsWith("Are they ", StringComparison.OrdinalIgnoreCase))
            return true;

        if (source.StartsWith("I ", StringComparison.Ordinal)
            || source.StartsWith("I'm ", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("I'll ", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("I've ", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("We ", StringComparison.Ordinal)
            || source.StartsWith("Welcome ", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("Hello", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("Good morning", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("Good afternoon", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("Good evening", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("Goodbye", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    internal static void Record(string source, string translated)
    {
        using var scope = TraceLog.Scope("NpcTalkStore.Record");
        if (!_saveNew.Value || string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(translated))
        {
            TraceLog.Counter("NpcTalkStore.Record.skip");
            return;
        }

        if (!IsEligibleTalkEntry(source))
        {
            TraceLog.Counter("NpcTalkStore.Record.skipIneligible");
            TalkLog.Info($"NpcTalk: ignorado (nao e fala Falar): \"{TalkLog.Preview(source)}\"");
            return;
        }

        if (string.Equals(source, translated, StringComparison.Ordinal))
        {
            TraceLog.Counter("NpcTalkStore.Record.skipIdentical");
            return;
        }

        lock (Lock)
        {
            var key = NormalizeKey(source);
            if (_entries.ContainsKey(key))
            {
                TraceLog.Counter("NpcTalkStore.Record.skipExists");
                return;
            }

            _entries[key] = translated;
            TraceLog.Event($"NpcTalkStore.Record.write path={_path} entries={_entries.Count}");
            WriteJsonFile(_path, _entries);
        }

        TraceLog.Counter("NpcTalkStore.Record.written");
        TalkLog.Info($"NpcTalk: nova fala salva ({Count} entradas)");
    }

    private static string ResolvePath()
    {
        using var scope = TraceLog.Scope("NpcTalkStore.ResolvePath");
        var path = GameDataPaths.NpcTalkPath;
        TraceLog.Event($"NpcTalkStore.ResolvePath path={path} exists={File.Exists(path)}");
        return path;
    }

    private static void MaybeMigrateLegacyYaml()
    {
        // Legado npcs.yaml misturava subtexto/vendor — nao migrar mais para npctalk.
        TraceLog.Event("NpcTalkStore.MaybeMigrateLegacyYaml.skip disabled");
    }

    private static Dictionary<string, string> LoadJsonFile(string path)
    {
        using var scope = TraceLog.Scope("NpcTalkStore.LoadJsonFile");
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            TraceLog.Event($"NpcTalkStore.LoadJsonFile missing path={path}");
            return entries;
        }

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (data == null)
                return entries;

            foreach (var kv in data)
            {
                if (string.IsNullOrEmpty(kv.Key))
                    continue;
                entries[NormalizeKey(kv.Key)] = kv.Value ?? "";
            }
        }
        catch (Exception ex)
        {
            TraceLog.Error("NpcTalkStore.LoadJsonFile", ex);
            TalkLog.Warn($"Erro ao ler {FileName}: {ex.Message}");
        }

        TraceLog.Event($"NpcTalkStore.LoadJsonFile done entries={entries.Count}");
        return entries;
    }

    private static void WriteJsonFile(string path, Dictionary<string, string> entries)
    {
        using var scope = TraceLog.Scope("NpcTalkStore.WriteJsonFile");
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var sorted = entries
            .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);

        var payload = JsonSerializer.Serialize(sorted, JsonWriteOptions);
        File.WriteAllText(path, payload + Environment.NewLine, Encoding.UTF8);
        TraceLog.Event($"NpcTalkStore.WriteJsonFile done path={path} entries={entries.Count}");
    }

    private static string NormalizeKey(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd();

    private static bool TryGetWritStatsTemplate(string source, out string translated)
    {
        translated = "";
        if (!source.Contains(WritStatsMarker, StringComparison.Ordinal))
            return false;

        var scoreMatch = CommunityScoreEn.Match(source);
        if (!scoreMatch.Success)
            return false;

        var score = scoreMatch.Groups[1].Value;
        foreach (var kv in _entries)
        {
            if (!kv.Key.Contains(WritStatsMarker, StringComparison.Ordinal))
                continue;

            translated = CommunityScorePt.Replace(kv.Value, $"Pontuação da comunidade: {score}");
            return true;
        }

        return false;
    }
}

#endregion

#region Translate
// --- GoogleAsyncQueue.cs ---
/// <summary>
/// Google em background — grava em TalkTurn/NpcYaml; próximo ShowTalk aplica via cache.
/// </summary>
internal static class GoogleAsyncQueue
{
    private static readonly ConcurrentDictionary<string, byte> InFlight = new(StringComparer.Ordinal);

    internal static void EnqueueBatch(
        List<int> pendingIndices,
        List<string> pendingTexts,
        List<TextKind> pendingKinds)
    {
        using var scope = TraceLog.Scope("GoogleAsyncQueue.EnqueueBatch");
        TraceLog.Event($"GoogleAsyncQueue.EnqueueBatch pending={pendingTexts.Count}");
        for (var i = 0; i < pendingTexts.Count; i++)
        {
            var source = pendingTexts[i];
            var kind = pendingKinds[i];
            if (string.IsNullOrWhiteSpace(source))
            {
                TraceLog.Counter("GoogleAsyncQueue.skip.blank");
                continue;
            }

            if (!InFlight.TryAdd(source, 0))
            {
                TraceLog.Counter("GoogleAsyncQueue.skip.inFlight");
                continue;
            }

            TraceLog.Counter("GoogleAsyncQueue.enqueue");
            TraceLog.Event($"GoogleAsyncQueue.enqueue kind={kind} preview=\"{TalkLog.Preview(source)}\"");
            TalkLog.Info($"Google async: enfileirado ({kind}) \"{TalkLog.Preview(source)}\"");
            Task.Run(() => RunOne(source, kind));
        }
    }

    private static void RunOne(string source, TextKind kind)
    {
        using var scope = TraceLog.Scope("GoogleAsyncQueue.RunOne");
        TraceLog.Event($"GoogleAsyncQueue.RunOne.begin kind={kind} preview=\"{TalkLog.Preview(source)}\"");
        try
        {
            var translated = GoogleTranslate.Translate(source);
            if (string.IsNullOrEmpty(translated) || translated == source)
            {
                TraceLog.Counter("GoogleAsyncQueue.RunOne.unchanged");
                TalkLog.GoogleSkipped(kind, source, "Google async vazio ou identico");
                return;
            }

            TraceLog.Counter("GoogleAsyncQueue.RunOne.translated");
            TalkTurn.Store(source, translated);
            NpcTalkStore.Record(source, translated);
            TalkLog.GoogleTranslated(kind, source, translated);
        }
        catch (Exception ex)
        {
            TraceLog.Error("GoogleAsyncQueue.RunOne", ex);
            TalkLog.Warn($"Google async falhou: {ex.Message}");
        }
        finally
        {
            InFlight.TryRemove(source, out _);
            TraceLog.Event("GoogleAsyncQueue.RunOne.end");
        }
    }
}

// --- GoogleTranslate.cs ---
internal static class GoogleTranslate
{
    private const string TranslateUrl = "https://translate.googleapis.com/translate_a/single";
    private const string UserAgent = "PgTranslateLive/0.7.9";

    private static readonly Regex PlaceholderPattern = new(
        @"(\\n|<[^>]+>|\{[^}]+\}|\$\d+|%[sdif]|<<<[^>]+>>>)",
        RegexOptions.Compiled);

    internal static string? Translate(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return source;

        try
        {
            var (protectedText, tokens) = ProtectPlaceholders(source);
            string translated;

            if (protectedText.Length > 4500)
            {
                var chunks = SplitChunks(protectedText);
                var parts = new List<string>(chunks.Count);
                foreach (var chunk in chunks)
                {
                    var part = CallGoogle(chunk);
                    if (part == null)
                        return null;
                    parts.Add(RestorePlaceholders(part, tokens));
                }

                translated = string.Join("\\n", parts);
            }
            else
            {
                var apiResult = CallGoogle(protectedText);
                if (apiResult == null)
                    return null;
                translated = RestorePlaceholders(apiResult, tokens);
            }

            return translated == source ? null : translated;
        }
        catch (Exception ex)
        {
            TalkLog.Warn($"Google Translate falhou: {ex.Message}");
            return null;
        }
    }

    private static string? CallGoogle(string text)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var query = Uri.EscapeDataString(text);
                var url = $"{TranslateUrl}?client=gtx&sl=en&tl=pt&dt=t&q={query}";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

                using var response = TranslateClient.SharedHttp.SendAsync(request).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    TalkLog.Warn($"Google HTTP {(int)response.StatusCode}");
                    return null;
                }

                var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                    return null;

                var segments = doc.RootElement[0];
                if (segments.ValueKind != JsonValueKind.Array)
                    return null;

                var sb = new System.Text.StringBuilder();
                foreach (var segment in segments.EnumerateArray())
                {
                    if (segment.ValueKind == JsonValueKind.Array
                        && segment.GetArrayLength() > 0
                        && segment[0].ValueKind == JsonValueKind.String)
                    {
                        sb.Append(segment[0].GetString());
                    }
                }

                return sb.Length > 0 ? sb.ToString() : null;
            }
            catch (Exception) when (attempt < 2)
            {
                System.Threading.Thread.Sleep(500 * (attempt + 1));
            }
        }

        return null;
    }

    private static (string protectedText, List<string> tokens) ProtectPlaceholders(string text)
    {
        var tokens = new List<string>();
        var protectedText = PlaceholderPattern.Replace(text, match =>
        {
            tokens.Add(match.Value);
            return $"__PH{tokens.Count - 1}__";
        });
        return (protectedText, tokens);
    }

    private static string RestorePlaceholders(string text, List<string> tokens)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            text = text.Replace($"__PH{i}__", tokens[i]);
            text = text.Replace($"__ PH {i} __", tokens[i]);
            text = text.Replace($"__PH {i}__", tokens[i]);
        }

        return text;
    }

    private static List<string> SplitChunks(string protectedText)
    {
        var chunks = new List<string>();
        var chunk = "";
        foreach (var line in protectedText.Split("\\n"))
        {
            var candidate = chunk.Length > 0 ? $"{chunk}\\n{line}" : line;
            if (candidate.Length > 4000 && chunk.Length > 0)
            {
                chunks.Add(chunk);
                chunk = line;
            }
            else
            {
                chunk = candidate;
            }
        }

        if (chunk.Length > 0)
            chunks.Add(chunk);

        return chunks;
    }
}

// --- TranslateClient.cs ---
internal enum TextKind
{
    General,
    Dialogue,
    Choice,
}

internal static class TranslateClient
{
    internal static readonly HttpClient SharedHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<int> _timeoutMs = null!;
    private static ConfigEntry<int> _minLength = null!;
    private static ConfigEntry<int> _minDialogueLength = null!;
    private static ConfigEntry<int> _minChoiceLength = null!;
    private static ConfigEntry<bool> _talkVerbose = null!;
    private static ConfigEntry<bool> _useGoogle = null!;

    internal static bool Enabled => _enabled?.Value ?? false;
    internal static bool LogVerbose => _talkVerbose?.Value ?? false;
    internal static bool UseGoogle => _useGoogle?.Value ?? true;

    internal static void Init(ConfigFile config)
    {
        using var scope = TraceLog.Scope("TranslateClient.Init.inner");
        _enabled = config.Bind("General", "Enabled", true,
            "Traduz dialogos de NPC (Falar) via Google.");
        _timeoutMs = config.Bind("General", "TimeoutMs", 30000,
            "Timeout HTTP em ms (Google pode levar 10-30 s em batch).");
        _minLength = config.Bind("General", "MinLength", 3,
            "Ignora strings curtas (UI generica).");
        _minDialogueLength = config.Bind("Dialogue", "MinLength", 1,
            "Minimo de caracteres para traduzir falas de NPC.");
        _minChoiceLength = config.Bind("Dialogue", "MinChoiceLength", 3,
            "Minimo de caracteres para traduzir botoes de escolha.");
        _talkVerbose = config.Bind("General", "TalkVerbose", false,
            "Logs detalhados do fluxo Falar (desligado = melhor performance).");
        _useGoogle = config.Bind("General", "UseGoogle", true,
            "Se true, traduz falas novas via Google em tempo real (Falar).");

        SharedHttp.Timeout = TimeSpan.FromMilliseconds(Math.Max(1000, _timeoutMs.Value));
        TraceLog.Event($"TranslateClient.Init enabled={Enabled} useGoogle={UseGoogle} timeoutMs={_timeoutMs.Value}");
    }

    internal static string[] ApplyFromTurn(
        string[] texts,
        TextKind[] kinds,
        LivePhase phase)
    {
        using var scope = TraceLog.Scope("TranslateClient.ApplyFromTurn");
        if (!Enabled || texts == null || kinds == null || texts.Length == 0)
            return Array.Empty<string>();

        var speeches = CollectByKind(texts, kinds, TextKind.Dialogue);
        var choices = CollectByKind(texts, kinds, TextKind.Choice);
        var general = CollectByKind(texts, kinds, TextKind.General);
        TalkLog.PhaseApplyOnly(phase, speeches.Count > 0 ? speeches : general, choices);

        TraceLog.Event($"TranslateClient.ApplyFromTurn phase={phase} texts={texts.Length}");
        return TalkTurn.Apply(texts, kinds);
    }

    internal static string[] TryTranslateBatch(
        string[] texts,
        TextKind[] kinds,
        LivePhase? phase = null)
    {
        using var scope = TraceLog.Scope("TranslateClient.TryTranslateBatch");
        if (!Enabled || texts == null || kinds == null || texts.Length == 0)
        {
            TraceLog.Counter("TranslateClient.TryTranslateBatch.skip");
            return Array.Empty<string>();
        }

        if (texts.Length != kinds.Length)
            throw new ArgumentException("texts and kinds length mismatch");

        TraceLog.Event($"TranslateClient.TryTranslateBatch phase={phase} texts={texts.Length}");

        var speeches = CollectByKind(texts, kinds, TextKind.Dialogue);
        var choices = CollectByKind(texts, kinds, TextKind.Choice);
        var general = CollectByKind(texts, kinds, TextKind.General);

        if (phase.HasValue)
        {
            var primary = speeches.Count > 0 ? speeches : general;
            TalkLog.PhaseCollect(phase.Value, primary, choices);
        }

        var results = new string[texts.Length];
        var pendingIndices = new List<int>();
        var pendingTexts = new List<string>();
        var pendingKinds = new List<TextKind>();
        var speechPending = new List<string>();
        var choicePending = new List<string>();
        var jsonHits = 0;

        for (var i = 0; i < texts.Length; i++)
        {
            var text = texts[i];
            var kind = kinds[i];
            if (string.IsNullOrWhiteSpace(text) || !PassesMinLength(text, kind))
            {
                TraceLog.Counter("TranslateClient.skip.minLengthOrBlank");
                continue;
            }

            if (kind == TextKind.Choice && LooksAlreadyTranslated(text))
            {
                TraceLog.Counter("TranslateClient.skip.choiceAlreadyTranslated");
                continue;
            }

            if (kind == TextKind.General && LooksAlreadyTranslated(text))
            {
                TraceLog.Counter("TranslateClient.skip.generalAlreadyTranslated");
                continue;
            }

            if (NpcTalkStore.TryGet(text, out var cached, out var npctalkMode))
            {
                TraceLog.Counter("TranslateClient.hit.npcYaml");
                TalkTurn.Store(text, cached);
                results[i] = cached;
                jsonHits++;
                var sourceFile = npctalkMode == "template"
                    ? "strings_npctalk.json (template)"
                    : "strings_npctalk.json";
                TalkLog.JsonResolved(kind, text, cached, sourceFile);
                continue;
            }

            if (CdnStringStore.TryGet(text, out cached, out var cdnSource))
            {
                TraceLog.Counter("TranslateClient.hit.cdnFallback");
                TalkTurn.Store(text, cached);
                results[i] = cached;
                jsonHits++;
                TalkLog.JsonResolved(kind, text, cached, cdnSource);
                continue;
            }

            if (TalkTurn.TryGet(text, out var sessionCached))
            {
                TraceLog.Counter("TranslateClient.hit.session");
                results[i] = sessionCached;
                jsonHits++;
                TalkLog.JsonSessionHit(kind, text, sessionCached);
                continue;
            }

            TalkLog.JsonNotFound(kind, text);

            pendingIndices.Add(i);
            pendingTexts.Add(text);
            pendingKinds.Add(kind);
            TraceLog.Counter("TranslateClient.pending");

            if (kind == TextKind.Dialogue)
                speechPending.Add(text);
            else if (kind == TextKind.Choice)
                choicePending.Add(text);
        }

        if (pendingIndices.Count == 0)
        {
            TalkLog.BatchCompleteJsonOnly(jsonHits);
            TraceLog.Event($"TranslateClient.TryTranslateBatch.done allCached jsonHits={jsonHits}");
            return results;
        }

        if (!UseGoogle)
        {
            TraceLog.Event($"TranslateClient.TryTranslateBatch.done useGoogle=false pending={pendingIndices.Count}");
            TalkLog.FlowGooglePath();
            for (var j = 0; j < pendingIndices.Count; j++)
                TalkLog.GoogleSkipped(pendingKinds[j], pendingTexts[j], "UseGoogle=false");
            return results;
        }

        TalkLog.GoogleBatch(speechPending, choicePending);
        TraceLog.Event($"TranslateClient.TryTranslateBatch.googleSync pending={pendingIndices.Count} speech={speechPending.Count} choice={choicePending.Count}");

        for (var j = 0; j < pendingIndices.Count; j++)
        {
            var idx = pendingIndices[j];
            var source = pendingTexts[j];
            var kind = pendingKinds[j];
            var translated = GoogleTranslate.Translate(source);
            if (string.IsNullOrEmpty(translated) || translated == source)
            {
                TraceLog.Counter("TranslateClient.googleSync.unchanged");
                TalkLog.GoogleSkipped(kind, source, "vazio ou identico");
                continue;
            }

            TraceLog.Counter("TranslateClient.googleSync.translated");
            TalkTurn.Store(source, translated);
            NpcTalkStore.Record(source, translated);
            results[idx] = translated;
            TalkLog.GoogleTranslated(kind, source, translated);
        }

        TraceLog.Event($"TranslateClient.TryTranslateBatch.done googleSync pending={pendingIndices.Count}");
        return results;
    }

    internal static bool PassesMinLength(string text, TextKind kind) =>
        PassesMinLengthInternal(text, kind);

    private static bool LooksAlreadyTranslated(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        foreach (var c in text)
        {
            if (c is 'á' or 'à' or 'â' or 'ã' or 'é' or 'ê' or 'í' or 'ó' or 'ô' or 'õ' or 'ú' or 'ç'
                or 'Á' or 'À' or 'Â' or 'Ã' or 'É' or 'Ê' or 'Í' or 'Ó' or 'Ô' or 'Õ' or 'Ú' or 'Ç')
                return true;
        }

        return false;
    }

    private static List<string> CollectByKind(string[] texts, TextKind[] kinds, TextKind kind) =>
        texts.Where((t, i) => kinds[i] == kind && !string.IsNullOrWhiteSpace(t)).ToList();

    private static bool PassesMinLengthInternal(string text, TextKind kind)
    {
        var minLen = kind switch
        {
            TextKind.Dialogue => _minDialogueLength.Value,
            TextKind.Choice => _minChoiceLength.Value,
            _ => _minLength.Value,
        };
        return text.Length >= minLen;
    }
}

#endregion

#region Npc
// --- EntityNpcIdCache.cs ---
/// <summary>
/// Aprende entityId/nome → npcId quando o jogo revela o id (ex.: ProcessStartInteraction).
/// </summary>
internal static class EntityNpcIdCache
{
    private static readonly object Lock = new();
    private static readonly Dictionary<int, string> ByEntity = new();
    private static readonly Dictionary<string, string> ByName = new(StringComparer.OrdinalIgnoreCase);

    internal static void Remember(int entityId, string? displayName, string npcId)
    {
        if (string.IsNullOrWhiteSpace(npcId) || !NpcIdPatterns.LooksLikeNpcId(npcId))
            return;

        npcId = npcId.Trim();
        lock (Lock)
        {
            if (entityId > 0)
                ByEntity[entityId] = npcId;

            if (!string.IsNullOrWhiteSpace(displayName))
                ByName[displayName.Trim()] = npcId;
        }

        TraceLog.Event(
            $"EntityNpcIdCache.remember entity={entityId} name={displayName} id={npcId}");
    }

    internal static bool TryGetByEntity(int entityId, out string npcId)
    {
        npcId = "";
        if (entityId <= 0)
            return false;

        lock (Lock)
            return ByEntity.TryGetValue(entityId, out npcId!);
    }

    internal static bool TryGetByName(string displayName, out string npcId)
    {
        npcId = "";
        if (string.IsNullOrWhiteSpace(displayName))
            return false;

        lock (Lock)
            return ByName.TryGetValue(displayName.Trim(), out npcId!);
    }
}

internal static class NpcIdPatterns
{
    internal static bool LooksLikeNpcId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = value.Trim();
        return value.StartsWith("NPC_", StringComparison.Ordinal)
               || value.StartsWith("EventNPC_", StringComparison.Ordinal);
    }

    internal static bool IsCandidateToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 160)
            return false;

        return LooksLikeNpcId(value)
               || (value.Contains('_', StringComparison.Ordinal)
                   && !value.Contains(' ', StringComparison.Ordinal)
                   && char.IsLetter(value[0]));
    }
}

// --- NpcNameResolver.cs ---
/// <summary>
/// Resolve nome amigavel do NPC a partir do id (ex.: NPC_Mirraverre → Miraverre).
/// </summary>
internal static class NpcNameResolver
{
    private static readonly object Lock = new();
    private static Dictionary<string, string>? _byId;
    private static Dictionary<string, string>? _idByName;

    internal static string Resolve(string npcId)
    {
        if (string.IsNullOrWhiteSpace(npcId))
            return "";

        EnsureLoaded();
        if (_byId!.TryGetValue(npcId, out var name) && !string.IsNullOrWhiteSpace(name))
            return name;

        if (npcId.StartsWith("NPC_", StringComparison.Ordinal))
            return npcId[4..].Replace('_', ' ');

        return npcId.Replace('_', ' ');
    }

    internal static bool TryResolveId(string displayName, out string npcId)
    {
        npcId = "";
        if (string.IsNullOrWhiteSpace(displayName))
            return false;

        EnsureLoaded();
        return _idByName!.TryGetValue(displayName.Trim(), out npcId!);
    }

    internal static void Invalidate()
    {
        lock (Lock)
        {
            _byId = null;
            _idByName = null;
        }
    }

    private static void EnsureLoaded()
    {
        lock (Lock)
        {
            if (_byId != null)
                return;

            _byId = new Dictionary<string, string>(StringComparer.Ordinal);
            _idByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var dir = GameDataPaths.TranslationDir;
            LoadNpcInfo(Path.Combine(dir, "strings_npcs.json"));
            LoadRequested(Path.Combine(dir, "strings_requested.json"));
        }
    }

    private static void LoadNpcInfo(string path)
    {
        if (!File.Exists(path))
            return;

        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            if (map == null)
                return;

            foreach (var (key, value) in map)
            {
                if (!key.StartsWith("npcinfo_", StringComparison.Ordinal)
                    || !key.EndsWith("_Name", StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(value))
                    continue;

                var id = key["npcinfo_".Length..^"_Name".Length];
                _byId.TryAdd(id, value);
                _idByName.TryAdd(value.Trim(), id);
            }
        }
        catch (Exception ex)
        {
            TraceLog.Error("NpcNameResolver.LoadNpcInfo", ex);
        }
    }

    private static void LoadRequested(string path)
    {
        if (!File.Exists(path))
            return;

        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            if (map == null)
                return;

            foreach (var (key, value) in map)
            {
                if (!key.EndsWith("_Name", StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(value))
                    continue;

                var slash = key.LastIndexOf('/');
                if (slash < 0)
                    continue;

                var id = key[(slash + 1)..^"_Name".Length];
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                _byId.TryAdd(id, value);
                _idByName.TryAdd(value.Trim(), id);
            }
        }
        catch (Exception ex)
        {
            TraceLog.Error("NpcNameResolver.LoadRequested", ex);
        }
    }
}

#endregion

#region Talk
// --- InteractionSubtextCapture.cs ---
// LogPolicy.Reminder — DebugSubtexto e camadas 1-5; nao remover esta classe.
/// <summary>
/// Captura subtexto do painel de interacao no clique — separado da captura de nome.
/// SubtextDebug: uma linha de log por camada, com todos os campos testados.
/// </summary>
internal static class InteractionSubtextCapture
{
    private static readonly string[] SelectionPriorityMembers =
    {
        "EntityDescription", "entityDesc", "CustomEntityDesc",
        "Description", "description", "Desc", "desc",
        "NpcDesc", "npcDesc", "SubTitle", "subtitle",
        "name", "Name", "EntityID", "entityID", "entityId", "EntityId",
    };

    private static readonly string[] ControllerPriorityMembers =
    {
        "lastEntityDescription", "LastEntityDescription",
        "entityDesc", "EntityDesc", "CustomEntityDesc",
        "EntityDescription", "entityDescription",
        "pendingPreTalkScreenInfo", "PendingPreTalkScreenInfo",
    };

    private static readonly string[] PreTalkPriorityMembers =
    {
        "NpcDesc", "npcDesc", "Desc", "desc", "Description", "description",
        "SubTitle", "subtitle", "FriendlyName", "NpcName", "npcName",
        "NpcId", "npcId", "TargetNpcId", "targetNpcId",
    };

    private static readonly string[] ControllerUiMembers =
    {
        "FancyMenuHeading", "DialogTitle", "InteractorDesc", "InteractorSubtext",
        "InteractorSubtitle", "Subtitle", "subTitle", "InteractorName",
        "FancyMenuPanel", "MenuScreen", "DialogButtonPrefab", "FancyButtonPrefab",
        "DialogTitleSpacer", "Menu", "FancyMenuArea",
    };

    private const int MaxScanDepth = 5;
    private const int MaxScanNodes = 96;
    private const int MaxTmpResults = 24;
    private const int MaxLogChunk = 900;
    private const int MaxStringPreview = 80;

    private sealed class ProbeEntry
    {
        internal string Field = "";
        internal string Status = "";
        internal bool IsCandidate;
        internal string? CandidateText;
    }

    private sealed class LayerResult
    {
        internal string LayerId = "";
        internal string Title = "";
        internal string ObjectType = "";
        internal string Meta = "";
        internal List<ProbeEntry> Fields = new();
    }

    internal static bool TryCaptureFromClick(
        string hookName = "unknown",
        object? controllerHint = null,
        object? selectionHint = null)
    {
        if (TalkSession.Active)
            return false;

        selectionHint ??= TalkSession.LastSelection;
        var controller = controllerHint ?? UiInteractionFinder.GetCachedController();
        var controllerSource = controllerHint != null
            ? UiInteractionFinder.IsKnownController(controllerHint) ? "hook" : "hook-resolvido"
            : UiInteractionFinder.HasCachedController
                ? "cache"
                : controller != null
                    ? "finder"
                    : "ausente";

        var layers = BuildLayers(controller, selectionHint, controllerSource, controllerHint);
        var allEntries = FlattenEntries(layers);

        if (PluginSettings.SubtextDebug)
            LogLayers(hookName, layers);

        if (!string.IsNullOrWhiteSpace(TalkSession.NpcSubtext))
            return false;

        var best = FindBestCandidate(allEntries);
        if (best == null)
            return false;

        ApplyCaptured(best.CandidateText!, $"{best.LayerId}:{best.Field}");
        return true;
    }

    private sealed class RankedEntry
    {
        internal string LayerId = "";
        internal string Field = "";
        internal string Status = "";
        internal bool IsCandidate;
        internal string? CandidateText;
    }

    private static void ApplyCaptured(string subtext, string source)
    {
        TalkSession.SetSubtext(subtext);
        TalkLog.Subtexto(TalkLog.Preview(subtext), source);
        TraceLog.Event($"InteractionSubtextCapture.captured source={source} text={TalkLog.Preview(subtext)}");
        TalkSession.NotifySubtextCaptured();
    }

    private static List<LayerResult> BuildLayers(
        object? controller,
        object? selection,
        string controllerSource,
        object? hookInstance = null)
    {
        var layers = new List<LayerResult>();

        layers.Add(BuildSelectionLayer(selection));

        if (controller == null)
        {
            var hookType = hookInstance?.GetType().Name ?? "—";
            layers.Add(EmptyLayer("2-UIInteractionController", "UIInteractionController", $"(nao encontrado) hook={hookType} fonte={controllerSource}"));
            layers.Add(EmptyLayer("3-PreTalkScreenInfo", "PreTalkScreenInfo", "(controller ausente)"));
            layers.Add(EmptyLayer("4-UIInteractionController.ui", "UI refs", "(controller ausente)"));
            layers.Add(EmptyLayer("5-UIInteractionController.scanTMP", "scan TMP", "(controller ausente)"));
            return layers;
        }

        var controllerType = controller.GetType();
        var typeName = controllerType.Name;

        layers.Add(BuildControllerStringsLayer(controller, controllerType, typeName, controllerSource));
        layers.Add(BuildPreTalkLayer(controller, controllerType));
        layers.Add(BuildUiRefsLayer(controller, controllerType, typeName));
        layers.Add(BuildTmpScanLayer(controller, typeName));

        return layers;
    }

    private static LayerResult BuildSelectionLayer(object? selection)
    {
        if (selection == null)
            return EmptyLayer("1-UISelection", "UISelection", "(nao disponivel)");

        var layer = NewLayer("1-UISelection", "UISelection", selection.GetType().Name, "clique/selecao");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ProbePriorityMembers(layer.Fields, selection, selection.GetType(), seen, SelectionPriorityMembers, treatNameMembers: true);
        ProbeAllStringMembers(layer.Fields, selection, selection.GetType(), seen, treatNameMembers: true);
        ProbeAllIntMembers(layer.Fields, selection, selection.GetType(), seen);

        return layer;
    }

    private static LayerResult BuildControllerStringsLayer(
        object controller,
        Type controllerType,
        string typeName,
        string controllerSource)
    {
        var layer = NewLayer("2-UIInteractionController", "UIInteractionController", typeName, $"fonte={controllerSource}");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ProbePriorityMembers(layer.Fields, controller, controllerType, seen, ControllerPriorityMembers, treatNameMembers: false);
        ProbeAllStringMembers(layer.Fields, controller, controllerType, seen, treatNameMembers: false);

        return layer;
    }

    private static LayerResult BuildPreTalkLayer(object controller, Type controllerType)
    {
        object? pending;
        try
        {
            pending = ReadMemberValue(controller, controllerType, "pendingPreTalkScreenInfo")
                      ?? ReadMemberValue(controller, controllerType, "PendingPreTalkScreenInfo");
        }
        catch
        {
            return EmptyLayer("3-PreTalkScreenInfo", "PreTalkScreenInfo", "(inacessivel)");
        }

        if (pending == null)
            return EmptyLayer("3-PreTalkScreenInfo", "PreTalkScreenInfo", "(pendingPreTalkScreenInfo vazio)");

        var infoType = pending.GetType();
        var layer = NewLayer("3-PreTalkScreenInfo", "PreTalkScreenInfo", infoType.Name, "pendingPreTalkScreenInfo");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ProbePriorityMembers(layer.Fields, pending, infoType, seen, PreTalkPriorityMembers, treatNameMembers: true);
        ProbeAllStringMembers(layer.Fields, pending, infoType, seen, treatNameMembers: true);

        return layer;
    }

    private static LayerResult BuildUiRefsLayer(object controller, Type controllerType, string typeName)
    {
        var layer = NewLayer("4-UIInteractionController.ui", "UI refs", typeName, "TMP/componentes UI");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var memberName in ControllerUiMembers)
        {
            if (!seen.Add(memberName))
                continue;

            object? ui;
            try
            {
                ui = ReadMemberValue(controller, controllerType, memberName);
            }
            catch
            {
                layer.Fields.Add(new ProbeEntry { Field = memberName, Status = "(inacessivel)" });
                continue;
            }

            if (ui == null)
            {
                layer.Fields.Add(new ProbeEntry { Field = memberName, Status = "(vazio)" });
                continue;
            }

            layer.Fields.Add(new ProbeEntry { Field = memberName, Status = $"(objeto {ui.GetType().Name})" });

            var isNameField = memberName.Contains("Name", StringComparison.OrdinalIgnoreCase)
                              && !memberName.Contains("Desc", StringComparison.OrdinalIgnoreCase);

            if (Il2CppStringHelper.IsStringType(ui.GetType()))
            {
                layer.Fields.Add(ClassifyString($"{memberName}.string", Il2CppStringHelper.Read(ui), isNameField));
                continue;
            }

            var tmpText = ReadTmpTextDeep(ui, 0);
            layer.Fields.Add(ClassifyString($"{memberName}.m_text", tmpText, isNameField));
        }

        ProbeUiObjectRefs(layer.Fields, controller, controllerType, seen, depth: 0, maxDepth: 2);

        return layer;
    }

    private static LayerResult BuildTmpScanLayer(object controller, string typeName)
    {
        var layer = NewLayer("5-UIInteractionController.scanTMP", "scan TMP", typeName, "arvore completa TMP");
        var visited = 0;
        var found = new List<(string Path, string Text)>();
        CollectTmpTexts(controller, controller, 0, ref visited, ref found, typeName, MaxTmpResults);

        if (found.Count == 0)
        {
            layer.Fields.Add(new ProbeEntry { Field = "(nenhum)", Status = "(nenhum TMP com texto)" });
            return layer;
        }

        foreach (var (path, text) in found)
        {
            var shortPath = path.StartsWith(typeName + ".", StringComparison.Ordinal)
                ? path[(typeName.Length + 1)..]
                : path;
            layer.Fields.Add(ClassifyString(shortPath, text, treatAsName: false));
        }

        return layer;
    }

    private static LayerResult NewLayer(string id, string title, string objectType, string meta) =>
        new()
        {
            LayerId = id,
            Title = title,
            ObjectType = objectType,
            Meta = meta,
        };

    private static LayerResult EmptyLayer(string id, string title, string meta) =>
        new()
        {
            LayerId = id,
            Title = title,
            ObjectType = "—",
            Meta = meta,
            Fields = { new ProbeEntry { Field = "—", Status = meta } },
        };

    private static void LogLayers(string hookName, List<LayerResult> layers)
    {
        // LogPolicy.RulePath — etapa DebugSubtexto por camada; nao remover.
        var npc = string.IsNullOrWhiteSpace(TalkSession.NpcName) ? "(sem nome)" : TalkSession.NpcName;
        var totalFields = 0;
        var candidates = 0;
        foreach (var layer in layers)
        {
            totalFields += layer.Fields.Count;
            foreach (var field in layer.Fields)
            {
                if (field.IsCandidate)
                    candidates++;
            }
        }

        TalkLog.DebugSubtexto(
            $"hook={hookName} | npc={npc} | inicio | camadas={layers.Count} | campos={totalFields} | candidatos={candidates}");

        foreach (var layer in layers)
        {
            var body = FormatLayerFields(layer.Fields);
            var prefix = $"hook={hookName} | npc={npc} | camada={layer.LayerId} | tipo={layer.ObjectType} | {layer.Meta} | campos: ";
            EmitDebugChunks(prefix, body);
        }

        TraceLog.Event($"InteractionSubtextCapture.probe hook={hookName} layers={layers.Count} fields={totalFields} candidates={candidates}");
    }

    private static string FormatLayerFields(List<ProbeEntry> fields)
    {
        if (fields.Count == 0)
            return "(nenhum campo)";

        var body = new StringBuilder();
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
                body.Append(", ");
            body.Append(fields[i].Field).Append('=').Append(fields[i].Status);
        }

        return body.ToString();
    }

    private static List<RankedEntry> FlattenEntries(List<LayerResult> layers)
    {
        var all = new List<RankedEntry>();
        foreach (var layer in layers)
        {
            foreach (var field in layer.Fields)
            {
                all.Add(new RankedEntry
                {
                    LayerId = layer.LayerId,
                    Field = field.Field,
                    Status = field.Status,
                    IsCandidate = field.IsCandidate,
                    CandidateText = field.CandidateText,
                });
            }
        }

        return all;
    }

    private static void EmitDebugChunks(string prefix, string body)
    {
        if (prefix.Length + body.Length <= MaxLogChunk)
        {
            TalkLog.DebugSubtexto(prefix + body);
            return;
        }

        var chunkSize = MaxLogChunk - prefix.Length - 20;
        if (chunkSize < 120)
            chunkSize = 120;

        var parts = (int)Math.Ceiling(body.Length / (double)chunkSize);
        for (var i = 0; i < parts; i++)
        {
            var start = i * chunkSize;
            var len = Math.Min(chunkSize, body.Length - start);
            TalkLog.DebugSubtexto($"{prefix}parte {i + 1}/{parts}: {body.Substring(start, len)}");
        }
    }

    private static RankedEntry? FindBestCandidate(List<RankedEntry> entries)
    {
        RankedEntry? best = null;
        foreach (var entry in entries)
        {
            if (!entry.IsCandidate || string.IsNullOrWhiteSpace(entry.CandidateText))
                continue;

            if (best == null || entry.CandidateText!.Length > best.CandidateText!.Length)
                best = entry;
        }

        return best;
    }

    private static void ProbePriorityMembers(
        List<ProbeEntry> entries,
        object obj,
        Type type,
        HashSet<string> seen,
        string[] memberNames,
        bool treatNameMembers)
    {
        foreach (var memberName in memberNames)
        {
            if (!seen.Add(memberName))
                continue;

            var path = memberName;
            try
            {
                if (IsIntMemberName(memberName))
                {
                    var intValue = ReadIntMember(obj, type, memberName);
                    entries.Add(new ProbeEntry
                    {
                        Field = path,
                        Status = intValue.HasValue ? intValue.Value.ToString() : "(vazio)",
                    });
                    continue;
                }

                var value = ReadStringMemberAnyType(obj, type, memberName);
                var isName = treatNameMembers && IsNameMember(memberName);
                entries.Add(ClassifyString(path, value, isName));
            }
            catch
            {
                entries.Add(new ProbeEntry { Field = path, Status = "(inacessivel)" });
            }
        }
    }

    private static void ProbeAllStringMembers(
        List<ProbeEntry> entries,
        object obj,
        Type type,
        HashSet<string> seen,
        bool treatNameMembers)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            foreach (var prop in current.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                    continue;
                if (!Il2CppStringHelper.IsStringType(prop.PropertyType))
                    continue;
                if (!seen.Add(prop.Name))
                    continue;

                try
                {
                    var value = Il2CppStringHelper.Read(SafeGet(() => prop.GetValue(obj)));
                    var isName = treatNameMembers && IsNameMember(prop.Name);
                    entries.Add(ClassifyString($"ref.{prop.Name}", value, isName));
                }
                catch
                {
                    entries.Add(new ProbeEntry { Field = $"ref.{prop.Name}", Status = "(inacessivel)" });
                }
            }

            foreach (var field in current.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (!Il2CppStringHelper.IsStringType(field.FieldType))
                    continue;
                if (!seen.Add(field.Name))
                    continue;

                try
                {
                    var value = Il2CppStringHelper.Read(SafeGet(() => field.GetValue(obj)));
                    var isName = treatNameMembers && IsNameMember(field.Name);
                    entries.Add(ClassifyString($"ref.{field.Name}", value, isName));
                }
                catch
                {
                    entries.Add(new ProbeEntry { Field = $"ref.{field.Name}", Status = "(inacessivel)" });
                }
            }
        }
    }

    private static void ProbeAllIntMembers(List<ProbeEntry> entries, object obj, Type type, HashSet<string> seen)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            foreach (var prop in current.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0 || prop.PropertyType != typeof(int))
                    continue;
                if (!seen.Add(prop.Name))
                    continue;

                try
                {
                    var value = (int)prop.GetValue(obj)!;
                    entries.Add(new ProbeEntry { Field = $"int.{prop.Name}", Status = value.ToString() });
                }
                catch
                {
                    entries.Add(new ProbeEntry { Field = $"int.{prop.Name}", Status = "(inacessivel)" });
                }
            }

            foreach (var field in current.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (field.FieldType != typeof(int) || !seen.Add(field.Name))
                    continue;

                try
                {
                    var value = (int)field.GetValue(obj)!;
                    entries.Add(new ProbeEntry { Field = $"int.{field.Name}", Status = value.ToString() });
                }
                catch
                {
                    entries.Add(new ProbeEntry { Field = $"int.{field.Name}", Status = "(inacessivel)" });
                }
            }
        }
    }

    private static void ProbeUiObjectRefs(
        List<ProbeEntry> entries,
        object obj,
        Type type,
        HashSet<string> seen,
        int depth,
        int maxDepth)
    {
        if (depth >= maxDepth)
            return;

        for (var current = type; current != null; current = current.BaseType)
        {
            foreach (var prop in current.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                    continue;
                if (!IsUiRefType(prop.PropertyType))
                    continue;
                if (!seen.Add(prop.Name))
                    continue;

                ProbeUiMember(entries, obj, prop.Name, () => prop.GetValue(obj), prop.PropertyType, depth, maxDepth);
            }

            foreach (var field in current.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (!IsUiRefType(field.FieldType) || !seen.Add(field.Name))
                    continue;

                ProbeUiMember(entries, obj, field.Name, () => field.GetValue(obj), field.FieldType, depth, maxDepth);
            }
        }
    }

    private static void ProbeUiMember(
        List<ProbeEntry> entries,
        object owner,
        string memberName,
        Func<object?> read,
        Type memberType,
        int depth,
        int maxDepth)
    {
        object? nested;
        try
        {
            nested = read();
        }
        catch
        {
            entries.Add(new ProbeEntry { Field = $"ui.{memberName}", Status = "(inacessivel)" });
            return;
        }

        if (nested == null || nested is string || ReferenceEquals(nested, owner))
        {
            entries.Add(new ProbeEntry { Field = $"ui.{memberName}", Status = "(vazio)" });
            return;
        }

        entries.Add(new ProbeEntry { Field = $"ui.{memberName}", Status = $"(objeto {nested.GetType().Name})" });

        if (IsTmpType(nested.GetType()))
        {
            entries.Add(ClassifyString($"ui.{memberName}.m_text", ReadTmpText(nested), treatAsName: false));
            return;
        }

        if (Il2CppStringHelper.IsStringType(nested.GetType()))
        {
            entries.Add(ClassifyString($"ui.{memberName}.string", Il2CppStringHelper.Read(nested), treatAsName: false));
            return;
        }

        if (depth + 1 < maxDepth)
            ProbeUiObjectRefs(entries, nested, nested.GetType(), seen: new HashSet<string>(StringComparer.OrdinalIgnoreCase), depth + 1, maxDepth);
    }

    private static bool IsUiRefType(Type type) =>
        type.IsClass && type != typeof(string)
        && (IsTmpType(type)
            || type.Name.Contains("UI", StringComparison.Ordinal)
            || type.Name.Contains("Text", StringComparison.OrdinalIgnoreCase)
            || type.Name.Contains("Menu", StringComparison.OrdinalIgnoreCase)
            || type.Name.Contains("Dialog", StringComparison.OrdinalIgnoreCase));

    private static bool IsNameMember(string memberName) =>
        memberName is "name" or "Name" or "FriendlyName" or "NpcName" or "npcName"
        || (memberName.Contains("Name", StringComparison.OrdinalIgnoreCase)
            && !memberName.Contains("Desc", StringComparison.OrdinalIgnoreCase));

    private static bool IsIntMemberName(string memberName) =>
        memberName.Contains("EntityID", StringComparison.OrdinalIgnoreCase)
        || memberName.Contains("EntityId", StringComparison.OrdinalIgnoreCase)
        || memberName.Contains("entityId", StringComparison.Ordinal);

    private static int? ReadIntMember(object obj, Type type, string memberName)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var prop = current.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop?.CanRead == true && prop.PropertyType == typeof(int))
            {
                try
                {
                    return (int)prop.GetValue(obj)!;
                }
                catch
                {
                    return null;
                }
            }

            var field = current.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field?.FieldType == typeof(int))
            {
                try
                {
                    return (int)field.GetValue(obj)!;
                }
                catch
                {
                    return null;
                }
            }
        }

        return null;
    }

    private static ProbeEntry ClassifyString(string field, string? value, bool treatAsName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new ProbeEntry { Field = field, Status = "(vazio)" };

        var full = value.Trim();
        var preview = full.Length > MaxStringPreview ? full[..MaxStringPreview] + "..." : full;

        if (IsUiNoise(full))
            return new ProbeEntry { Field = field, Status = $"(ruido UI) \"{preview}\"" };

        var npcName = TalkSession.NpcName;
        var normalizedNameplate = TalkSession.NormalizeInteractorLabel(full);
        if (treatAsName
            || full.StartsWith("Nameplate:", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(npcName)
                && (full.Equals(npcName, StringComparison.OrdinalIgnoreCase)
                    || normalizedNameplate.Equals(npcName, StringComparison.OrdinalIgnoreCase))))
            return new ProbeEntry { Field = field, Status = $"(nome) \"{preview}\"" };

        if (full.Equals("Talk", StringComparison.OrdinalIgnoreCase)
            || full.Equals("Entity Name Here", StringComparison.OrdinalIgnoreCase))
            return new ProbeEntry { Field = field, Status = $"(ui) \"{preview}\"" };

        if (full.Length < 8)
            return new ProbeEntry { Field = field, Status = $"(curto) \"{preview}\"" };

        if (!IsValidSubtext(full))
            return new ProbeEntry { Field = field, Status = $"(texto) \"{preview}\"" };

        return new ProbeEntry
        {
            Field = field,
            Status = $"★ \"{preview}\"",
            IsCandidate = true,
            CandidateText = full,
        };
    }

    private static void CollectTmpTexts(
        object root,
        object current,
        int depth,
        ref int visited,
        ref List<(string Path, string Text)> found,
        string path,
        int maxResults)
    {
        if (depth > MaxScanDepth || visited >= MaxScanNodes || found.Count >= maxResults || current == null)
            return;

        visited++;
        var type = current.GetType();

        if (IsTmpType(type))
        {
            var text = ReadTmpText(current);
            if (!string.IsNullOrWhiteSpace(text))
                found.Add((path, text.Trim()));
            return;
        }

        for (var t = type; t != null && depth < MaxScanDepth; t = t.BaseType)
        {
            foreach (var field in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (field.FieldType == typeof(string) || field.FieldType.IsPrimitive)
                    continue;

                var nested = SafeGet(() => field.GetValue(current));
                if (nested == null || nested is string || ReferenceEquals(nested, root))
                    continue;

                CollectTmpTexts(root, nested, depth + 1, ref visited, ref found, $"{path}.{field.Name}", maxResults);
            }

            foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                    continue;
                if (prop.PropertyType == typeof(string) || prop.PropertyType.IsPrimitive)
                    continue;

                var nested = SafeGet(() => prop.GetValue(current));
                if (nested == null || nested is string || ReferenceEquals(nested, root))
                    continue;

                CollectTmpTexts(root, nested, depth + 1, ref visited, ref found, $"{path}.{prop.Name}", maxResults);
            }
        }
    }

    private static bool IsTmpType(Type type) =>
        type.Name.Contains("TextMeshPro", StringComparison.Ordinal)
        || type.Name.Contains("TMP_", StringComparison.Ordinal);

    private static string? ReadTmpTextDeep(object root, int depth)
    {
        if (depth > MaxScanDepth)
            return null;

        if (IsTmpType(root.GetType()))
            return ReadTmpText(root);

        var visited = 0;
        var found = new List<(string Path, string Text)>();
        CollectTmpTexts(root, root, depth, ref visited, ref found, "ui", maxResults: 1);
        return found.Count > 0 ? found[0].Text : null;
    }

    private static string? ReadTmpText(object tmp)
    {
        var type = tmp.GetType();
        for (var current = type; current != null; current = current.BaseType)
        {
            var prop = current.GetProperty("text", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop?.CanRead == true && Il2CppStringHelper.IsStringType(prop.PropertyType))
            {
                var value = Il2CppStringHelper.Read(SafeGet(() => prop.GetValue(tmp)));
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            var field = current.GetField("m_text", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && Il2CppStringHelper.IsStringType(field.FieldType))
            {
                var value = Il2CppStringHelper.Read(SafeGet(() => field.GetValue(tmp)));
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
        }

        return null;
    }

    private static bool IsValidSubtext(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim();
        if (text.Length < 8 || text.Length > 512)
            return false;

        if (IsUiNoise(text))
            return false;

        var npcName = TalkSession.NpcName;
        if (!string.IsNullOrWhiteSpace(npcName)
            && (text.Equals(npcName, StringComparison.OrdinalIgnoreCase)
                || TalkSession.NormalizeInteractorLabel(text).Equals(npcName, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (text.Equals("Talk", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Entity Name Here", StringComparison.OrdinalIgnoreCase))
            return false;

        return LooksLikeSubtext(text);
    }

    private static bool LooksLikeSubtext(string text) =>
        text.Length >= 20
        || (text.Contains(' ', StringComparison.Ordinal)
            && (text.Contains("look", StringComparison.OrdinalIgnoreCase)
                || text.Contains("seems", StringComparison.OrdinalIgnoreCase)
                || text.Contains("appears", StringComparison.OrdinalIgnoreCase)
                || text.Contains("bored", StringComparison.OrdinalIgnoreCase)
                || text.Contains("he ", StringComparison.OrdinalIgnoreCase)
                || text.Contains("she ", StringComparison.OrdinalIgnoreCase)));

    private static bool IsUiNoise(string text) =>
        text.Contains("DialogWindow", StringComparison.OrdinalIgnoreCase)
        || text.Contains("(Clone)", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Untagged", StringComparison.OrdinalIgnoreCase);

    private static string? ReadStringMemberAnyType(object obj, Type type, string memberName)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var prop = current.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop?.CanRead == true && Il2CppStringHelper.IsStringType(prop.PropertyType))
                return Il2CppStringHelper.Read(SafeGet(() => prop.GetValue(obj)))?.Trim();

            var field = current.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && Il2CppStringHelper.IsStringType(field.FieldType))
                return Il2CppStringHelper.Read(SafeGet(() => field.GetValue(obj)))?.Trim();
        }

        return null;
    }

    private static object? ReadMemberValue(object obj, Type type, string memberName)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var prop = current.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop?.CanRead == true)
                return SafeGet(() => prop.GetValue(obj));

            var field = current.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
                return SafeGet(() => field.GetValue(obj));
        }

        return null;
    }

    private static T? SafeGet<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return default;
        }
    }
}
// --- PreTalkScreenProbe.cs ---
// LogPolicy.Reminder — leitura de contexto NPC; manter logs de Interacao/subtexto.
/// <summary>
/// Extrai nome/subtexto do painel de interacao (PreTalkScreenInfo / UIInteractionController) via reflection Il2CPP.
/// </summary>
internal static class PreTalkScreenProbe
{
    private static readonly string[] IdMemberNames =
    {
        "NpcId", "npcId", "TargetNpcId", "targetNpcId", "EntityId", "entityId",
        "NpcEntityId", "npcEntityId", "TargetEntityId", "targetEntityId",
        "entityID", "EntityID",
    };

    private static readonly string[] EntityIdIntMemberNames =
    {
        "entityId", "EntityId", "entityID", "EntityID", "m_entityId", "npcEntityId",
    };

    private static readonly string[] NameMemberNames =
    {
        "NpcName", "npcName", "Name", "name", "FriendlyName", "friendlyName",
        "NpcFriendlyName", "ShortFriendlyName", "displayName", "DisplayName",
        "title", "Title",
    };

    private static readonly string[] DescMemberNames =
    {
        "NpcDesc", "npcDesc", "Desc", "desc", "Description", "description",
        "SubTitle", "subtitle", "TranslationDescription", "rawDescription",
        "FormattedDescription", "moodDescription", "MoodDescription",
        "preTalkDesc", "PreTalkDesc", "secondaryText", "SecondaryText",
        "flavorText", "FlavorText", "FancyMenuHeading", "fancyMenuHeading",
    };

    private static readonly string[] DescWriteMemberNames =
    {
        "NpcDesc", "Desc", "desc", "Description", "description",
        "SubTitle", "TranslationDescription", "FancyMenuHeading",
    };

    internal static void CaptureFromProcessPreTalk(object[]? args)
    {
        if (args == null || args.Length == 0)
            return;

        foreach (var arg in args)
        {
            if (arg == null || arg is string or int or long or float or double or bool)
                continue;

            if (TryCaptureObject(arg, out var name, out var desc))
            {
                ApplyNpcContext(name, desc, arg, null, logInteraction: false);
                return;
            }
        }
    }

    /// <summary>
    /// Clique no NPC / menu de interacao — mesma leitura do OnPreTalk, log em etapa Interacao.
    /// </summary>
    internal static void CaptureFromInteractionPanel(object? controller, bool logInteraction = true)
    {
        if (TalkSession.Active)
            return;

        TryPrimeNpcIdFromController(controller);

        if (!TryReadNpcContext(controller, null, out var name, out var desc))
        {
            TraceLog.Event(
                $"PreTalkScreenProbe.interaction.miss controller={controller?.GetType().FullName}");
            if (logInteraction && controller != null)
            {
                InteractionSubtextCapture.TryCaptureFromClick("CaptureFromInteractionPanel", controller);
                TalkSession.RegisterNpcIfReady();
            }

            return;
        }

        ApplyNpcContext(name, desc, controller, null, logInteraction);
    }

    internal static void PrimeSubtextFromController()
    {
        if (!string.IsNullOrWhiteSpace(TalkSession.NpcSubtext))
            return;

        var controller = UiInteractionFinder.GetController();
        TryPrimeNpcIdFromController(controller);

        if (!TryReadNpcContext(controller, null, out var name, out var desc))
            return;

        if (!string.IsNullOrWhiteSpace(name) && !IsUiNoiseName(name))
            TalkSession.SetDisplayName(name);
        if (!string.IsNullOrWhiteSpace(desc))
            TalkSession.SetSubtext(desc);
    }

    /// <summary>
    /// Clique/selecao no mundo (UISelection) — painel Talk antes do dialogo.
    /// </summary>
    internal static void CaptureFromSelection(object? selection)
    {
        if (selection == null || TalkSession.Active)
            return;

        TalkSession.BeginInteractionCapture();
        TalkSession.SetLastSelection(selection);

        TraceLog.Event($"PreTalkScreenProbe.selection type={selection.GetType().FullName}");

        if (TryReadEntityId(selection, out var entityId))
        {
            TalkSession.SetSelectedEntity(entityId);
            if (string.IsNullOrEmpty(TalkSession.NpcId)
                && EntityNpcIdCache.TryGetByEntity(entityId, out var cachedId))
            {
                TalkSession.SetNpc(cachedId);
                TalkLog.DebugNpcId($"cache hit entity={entityId} id={cachedId}");
            }
        }

        var npcId = ReadNpcId(selection);
        if (!string.IsNullOrWhiteSpace(npcId))
            TalkSession.SetNpc(npcId);

        if (TryCaptureObject(selection, out var name, out var desc))
        {
            ApplyNpcContext(name, desc, null, selection, logInteraction: false);
            return;
        }

        var controller = UiInteractionFinder.GetController();
        TryPrimeNpcIdFromController(controller);
        if (TryReadNpcContext(controller, null, out name, out desc))
        {
            ApplyNpcContext(name, desc, controller, selection, logInteraction: false);
        }
    }

    internal static void CaptureFromOnPreTalk(object? controller, object? info)
    {
        if (!TryReadNpcContext(controller, info, out var name, out var desc))
        {
            TraceLog.Event(
                $"PreTalkScreenProbe.miss controller={controller?.GetType().FullName} info={info?.GetType().FullName}");
            return;
        }

        ApplyNpcContext(name, desc, controller, info, logInteraction: false);
    }

    internal static void CaptureFromArgs(object[]? args)
    {
        if (args == null)
            return;

        foreach (var arg in args)
        {
            if (arg == null || arg is string or int or long or float or double or bool)
                continue;

            if (TryCaptureObject(arg, out var name, out var desc))
            {
                ApplyNpcContext(name, desc, arg, null, logInteraction: false);
                return;
            }
        }
    }

    private static bool TryReadNpcContext(
        object? controller,
        object? info,
        out string? name,
        out string? desc)
    {
        name = null;
        desc = null;

        if (info != null)
            TryCaptureObject(info, out name, out desc);

        if (controller != null)
            TryReadFromController(controller, ref name, ref desc);

        return !string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(desc);
    }

    private static void ApplyNpcContext(
        string? name,
        string? desc,
        object? controller,
        object? info,
        bool logInteraction)
    {
        if (!string.IsNullOrWhiteSpace(name) && !IsUiNoiseName(name))
            TalkSession.SetDisplayName(name);
        if (!string.IsNullOrWhiteSpace(desc))
            TalkSession.SetSubtext(desc);

        TraceLog.Event(
            $"PreTalkScreenProbe.captured name={name} desc={TalkLog.Preview(desc ?? "")} " +
            $"interaction={logInteraction} type={info?.GetType().FullName ?? controller?.GetType().FullName}");

        if (logInteraction)
            TalkSession.RegisterNpcIfReady();

        ApplySubtextTranslation(info, controller);
    }

    private static void ApplySubtextTranslation(object? info, object? controller)
    {
        if (string.IsNullOrWhiteSpace(TalkSession.NpcSubtext))
        {
            if (TalkSession.InteractionLogged)
                TalkSession.RegisterNpcIfReady();
            return;
        }

        var translated = NpcRegistryStore.ResolveSubtext(TalkSession.NpcId, TalkSession.NpcSubtext);
        if (string.IsNullOrWhiteSpace(translated) || translated == TalkSession.NpcSubtext)
        {
            TalkSession.RegisterNpcIfReady();
            return;
        }

        if (info != null)
            TryWriteDesc(info, translated);
        if (controller != null)
            TryWriteDesc(controller, translated);

        TalkLog.SubtextApplied(TalkSession.NpcSubtext, translated);
        TalkSession.RegisterNpcIfReady();
    }

    private static bool IsUiNoiseName(string name) =>
        name.Contains("DialogWindow", StringComparison.OrdinalIgnoreCase)
        || name.Contains("(Clone)", StringComparison.OrdinalIgnoreCase);

    private static void TryPrimeNpcIdFromController(object? controller)
    {
        if (controller == null || !string.IsNullOrEmpty(TalkSession.NpcId))
            return;

        var id = ReadNpcId(controller);
        if (string.IsNullOrWhiteSpace(id))
            return;

        TalkSession.SetNpc(id);
        TalkSession.PrimeFromPack();
    }

    private static string? ReadNpcId(object obj)
    {
        var type = obj.GetType();
        for (var current = type; current != null; current = current.BaseType)
        {
            var id = ReadFirstStringMember(obj, current, IdMemberNames);
            if (!string.IsNullOrWhiteSpace(id) && LooksLikeNpcId(id))
                return id.Trim();
        }

        return null;
    }

    private static bool LooksLikeNpcId(string value) => NpcIdPatterns.LooksLikeNpcId(value);

    private static bool TryReadEntityId(object obj, out int entityId)
    {
        entityId = -1;
        var type = obj.GetType();
        for (var current = type; current != null; current = current.BaseType)
        {
            foreach (var memberName in EntityIdIntMemberNames)
            {
                var prop = current.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop?.CanRead == true && prop.PropertyType == typeof(int))
                {
                    try
                    {
                        entityId = (int)prop.GetValue(obj)!;
                        if (entityId > 0)
                            return true;
                    }
                    catch
                    {
                        // Il2CPP property access can fail on stripped types.
                    }
                }

                var field = current.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field?.FieldType == typeof(int))
                {
                    try
                    {
                        entityId = (int)field.GetValue(obj)!;
                        if (entityId > 0)
                            return true;
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
        }

        return entityId > 0;
    }

    private static void TryReadFromController(object controller, ref string? name, ref string? desc)
    {
        var type = controller.GetType();
        if (string.IsNullOrWhiteSpace(name))
            name = ReadFirstStringMember(controller, type, NameMemberNames);
        if (string.IsNullOrWhiteSpace(desc))
            desc = ReadFirstStringMember(controller, type, DescMemberNames);
        TryReadPendingInfo(controller, ref name, ref desc);
    }

    private static void TryReadPendingInfo(object controller, ref string? name, ref string? desc)
    {
        var type = controller.GetType();
        var pending = ReadMember(controller, type, "pendingPreTalkScreenInfo")
                      ?? ReadMember(controller, type, "PendingPreTalkScreenInfo");
        if (pending == null)
            return;

        if (TryCaptureObject(pending, out var pendingName, out var pendingDesc))
        {
            if (string.IsNullOrWhiteSpace(name))
                name = pendingName;
            if (string.IsNullOrWhiteSpace(desc))
                desc = pendingDesc;
        }

        if (string.IsNullOrEmpty(TalkSession.NpcId))
        {
            var pendingId = ReadNpcId(pending);
            if (!string.IsNullOrWhiteSpace(pendingId))
            {
                TalkSession.SetNpc(pendingId);
                TalkSession.PrimeFromPack();
            }
        }
    }

    private static object? ReadMember(object obj, Type type, string memberName)
    {
        var prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (prop?.CanRead == true)
        {
            try
            {
                return prop.GetValue(obj);
            }
            catch
            {
                // Il2CPP property access can fail on stripped types.
            }
        }

        var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null)
            return null;

        try
        {
            return field.GetValue(obj);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryCaptureObject(object obj, out string? name, out string? desc)
    {
        name = null;
        desc = null;
        var type = obj.GetType();

        for (var current = type; current != null; current = current.BaseType)
        {
            if (string.IsNullOrWhiteSpace(name))
                name = ReadFirstStringMember(obj, current, NameMemberNames);
            if (string.IsNullOrWhiteSpace(desc))
                desc = ReadFirstStringMember(obj, current, DescMemberNames);
        }

        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(desc))
            ScanAllStringMembers(obj, type, ref name, ref desc);

        if (IsUiNoiseName(name ?? ""))
            name = null;

        return !string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(desc);
    }

    private static void ScanAllStringMembers(object obj, Type type, ref string? name, ref string? desc)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            foreach (var prop in current.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (!Il2CppStringHelper.IsStringType(prop.PropertyType) || !prop.CanRead)
                    continue;

                var value = Il2CppStringHelper.Read(SafeGet(() => prop.GetValue(obj)));
                AssignHeuristic(value, ref name, ref desc);
            }

            foreach (var field in current.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (!Il2CppStringHelper.IsStringType(field.FieldType))
                    continue;

                var value = Il2CppStringHelper.Read(SafeGet(() => field.GetValue(obj)));
                AssignHeuristic(value, ref name, ref desc);
            }
        }
    }

    private static void AssignHeuristic(string? value, ref string? name, ref string? desc)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512)
            return;

        if (IsUiNoiseName(value))
            return;

        if (LooksLikeDesc(value))
            desc ??= value;
        else if (value.Length < 64)
            name ??= value;
    }

    private static string? ReadFirstStringMember(object obj, Type type, string[] memberNames)
    {
        foreach (var memberName in memberNames)
        {
            var prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop?.CanRead == true && Il2CppStringHelper.IsStringType(prop.PropertyType))
            {
                var value = Il2CppStringHelper.Read(SafeGet(() => prop.GetValue(obj)));
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && Il2CppStringHelper.IsStringType(field.FieldType))
            {
                var value = Il2CppStringHelper.Read(SafeGet(() => field.GetValue(obj)));
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return null;
    }

    private static bool TryWriteDesc(object obj, string translated)
    {
        var type = obj.GetType();
        for (var current = type; current != null; current = current.BaseType)
        {
            foreach (var memberName in DescWriteMemberNames)
            {
                if (TryWriteStringMember(obj, current, memberName, translated))
                    return true;
            }
        }

        return false;
    }

    private static bool TryWriteStringMember(object obj, Type type, string memberName, string translated)
    {
        var prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (prop?.CanWrite == true && Il2CppStringHelper.IsStringType(prop.PropertyType))
        {
            try
            {
                prop.SetValue(obj, Il2CppStringHelper.Write(translated));
                TraceLog.Event($"PreTalkScreenProbe.wrote member={memberName} type={type.Name}");
                return true;
            }
            catch
            {
                // fall through
            }
        }

        var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null || !Il2CppStringHelper.IsStringType(field.FieldType))
            return false;

        try
        {
            field.SetValue(obj, Il2CppStringHelper.Write(translated));
            TraceLog.Event($"PreTalkScreenProbe.wrote field={memberName} type={type.Name}");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeDesc(string value) =>
        value.Length > 24
        || value.Contains(' ', StringComparison.Ordinal)
           && (value.Contains("look", StringComparison.OrdinalIgnoreCase)
               || value.Contains("seems", StringComparison.OrdinalIgnoreCase)
               || value.Contains("appears", StringComparison.OrdinalIgnoreCase)
               || value.Contains("bored", StringComparison.OrdinalIgnoreCase));

    private static T? SafeGet<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return default;
        }
    }
}
// --- TalkDiscovery.cs ---
/// <summary>
/// Modo discovery: patch leve em metodos cujo nome/tipo bate padroes de interacao;
/// grava deltas em discovery.log (metodo completo Type.Method).
/// </summary>
internal static class TalkDiscovery
{
    private static readonly string[] MethodNameTokens =
    {
        "Interact", "Interaction", "Click", "Select", "Target", "Entity",
        "Npc", "Fancy", "PreTalk", "Talk", "Menu", "Open", "Start",
    };

    private static readonly string[] TypeNameTokens =
    {
        "Interaction", "Entity", "Click", "Npc", "AreaCmd",
        "Fancy", "PreTalk", "Talk", "Menu", "UI",
    };

    private static readonly string[] ExcludedMethodNames =
    {
        "Update", "FixedUpdate", "LateUpdate", "OnEnable", "OnDisable",
        "Awake", "OnDestroy", "OnGUI", "MoveNext", "ToString", "Equals",
        "GetHashCode", "Finalize", "Dispose", "GetEnumerator",
    };

    private static readonly ConcurrentDictionary<string, long> Counts = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, long> LastSnapshot = new(StringComparer.Ordinal);
    private static CancellationTokenSource? _cts;
    private static string _logPath = "";
    private static int _patchedMethods;

    internal static int PatchedMethods => _patchedMethods;

    internal static int PatchAll(Harmony harmony)
    {
        var probe = new HarmonyMethod(typeof(TextPatchHelper), nameof(TextPatchHelper.DiscoveryProbePrefix));
        var patched = new HashSet<MethodBase>();
        var maxMethods = Math.Max(50, PluginSettings.DiscoveryMaxMethods);
        var matchCount = 0;
        var skipCount = 0;

        foreach (var type in AccessTools.AllTypes())
        {
            if (type.IsInterface || type.IsGenericTypeDefinition || type.ContainsGenericParameters)
                continue;

            if (!TypeMatches(type))
                continue;

            foreach (var method in AccessTools.GetDeclaredMethods(type))
            {
                if (method.IsAbstract || method.ContainsGenericParameters)
                    continue;
                if (!MethodMatches(method))
                    continue;
                if (!patched.Add(method))
                    continue;

                try
                {
                    harmony.Patch(method, prefix: probe);
                    matchCount++;
                    TraceLog.Event($"Discovery.patch {type.FullName}.{method.Name}");
                }
                catch (Exception ex)
                {
                    skipCount++;
                    TraceLog.Event($"Discovery.skip {type.FullName}.{method.Name} {ex.GetType().Name}");
                }

                if (matchCount >= maxMethods)
                {
                    LogAscii.LogWarning($"  discovery cap atingido ({maxMethods} metodos)");
                    break;
                }
            }

            if (matchCount >= maxMethods)
                break;
        }

        _patchedMethods = matchCount;
        TraceLog.Event($"Discovery.patch.done count={matchCount} skipped={skipCount}");
        if (skipCount > 0)
            LogAscii.LogInfo($"  discovery: {matchCount} patches, {skipCount} ignorados (genericos/IL2CPP)");
        return matchCount;
    }

    internal static void Start(int intervalSec)
    {
        _logPath = Path.Combine(Paths.BepInExRootPath, "plugins", "PgTranslateLive", "discovery.log");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
        }
        catch
        {
            // ignore
        }

        _cts = new CancellationTokenSource();
        WriteHeader();

        Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(2, intervalSec)), _cts.Token)
                        .ConfigureAwait(false);
                    WriteSnapshot();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogAscii.LogWarning($"TalkDiscovery: {ex.Message}");
                }
            }
        });
    }

    internal static void Hit(MethodBase method)
    {
        var key = FormatKey(method);
        Counts.AddOrUpdate(key, 1, (_, current) => current + 1);
    }

    private static bool TypeMatches(Type type)
    {
        var name = type.Name;
        for (var i = 0; i < TypeNameTokens.Length; i++)
        {
            if (name.Contains(TypeNameTokens[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool MethodMatches(MethodInfo method)
    {
        var name = method.Name;
        if (name.StartsWith("get_", StringComparison.Ordinal)
            || name.StartsWith("set_", StringComparison.Ordinal)
            || name.StartsWith("add_", StringComparison.Ordinal)
            || name.StartsWith("remove_", StringComparison.Ordinal)
            || name is ".ctor" or ".cctor")
            return false;

        for (var i = 0; i < ExcludedMethodNames.Length; i++)
        {
            if (string.Equals(name, ExcludedMethodNames[i], StringComparison.Ordinal))
                return false;
        }

        for (var i = 0; i < MethodNameTokens.Length; i++)
        {
            if (name.Contains(MethodNameTokens[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string FormatKey(MethodBase method) =>
        $"{method.DeclaringType?.FullName ?? "?"}.{method.Name}";

    private static void WriteHeader()
    {
        var line =
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} DISCOVERY — discovery.log " +
            $"(intervalo {PluginSettings.DiscoveryIntervalSec}s, max {PluginSettings.DiscoveryMaxMethods} patches)\r\n" +
            "Somente metodos com +delta no intervalo. Formato: +N TypeFullName.MethodName\r\n";
        File.AppendAllText(_logPath, line);
    }

    private static void WriteSnapshot()
    {
        var hits = new List<(string Key, long Delta)>();
        foreach (var pair in Counts)
        {
            var total = pair.Value;
            var last = LastSnapshot.TryGetValue(pair.Key, out var prev) ? prev : 0L;
            var delta = total - last;
            LastSnapshot[pair.Key] = total;
            if (delta > 0)
                hits.Add((pair.Key, delta));
        }

        if (hits.Count == 0)
        {
            File.AppendAllText(_logPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} (sem hits no intervalo)\r\n");
            return;
        }

        hits.Sort((a, b) => b.Delta.CompareTo(a.Delta));
        var lines = new List<string>
        {
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} hits={hits.Count}",
        };

        foreach (var (key, delta) in hits.Take(40))
            lines.Add($"+{delta} {key}");

        if (hits.Count > 40)
            lines.Add($"... +{hits.Count - 40} metodos omitidos");

        File.AppendAllText(_logPath, string.Join("\r\n", lines) + "\r\n");
    }
}

// --- TalkDynamicPatch.cs ---
/// <summary>
/// Aplica/remove patch de tradução em ShowTalk + DisplayTalkScreen só durante diálogo.
/// </summary>
internal static class TalkDynamicPatch
{
    private const string HarmonyId = "com.pg.translatelive";

    private static Harmony? _harmony;
    private static readonly List<MethodInfo> TranslationMethods = new();
    private static bool _applied;

    internal static void Init(Harmony harmony, IEnumerable<MethodInfo> translationMethods)
    {
        using var scope = TraceLog.Scope("TalkDynamicPatch.Init");
        _harmony = harmony;
        TranslationMethods.Clear();
        TranslationMethods.AddRange(translationMethods);
        TraceLog.Event($"TalkDynamicPatch.Init methods={TranslationMethods.Count}");
    }

    internal static bool IsApplied => _applied;

    internal static void ApplyTranslationPatches()
    {
        using var scope = TraceLog.Scope("TalkDynamicPatch.ApplyTranslationPatches");
        if (_harmony == null || _applied || TranslationMethods.Count == 0)
        {
            TraceLog.Event($"TalkDynamicPatch.Apply.skip harmonyNull={_harmony == null} applied={_applied} methods={TranslationMethods.Count}");
            return;
        }

        var count = 0;
        foreach (var method in TranslationMethods)
        {
            if (IsTranslationPatched(method) || !HarmonyPatchGuard.TryClaim(method))
            {
                TraceLog.Counter("TalkDynamicPatch.Apply.alreadyPatched");
                continue;
            }

            _harmony.Patch(method, prefix: PrefixFor(method));
            TraceLog.Event($"TalkDynamicPatch.Apply.patch {method.DeclaringType?.FullName}.{method.Name}");
            count++;
        }

        if (count > 0)
        {
            _applied = true;
            TalkDiagnostics.HitDynamicApply();
            LogAscii.LogInfo($"  dynamic patch ON ({count} metodos traducao)");
        }
    }

    internal static void RemoveTranslationPatches()
    {
        using var scope = TraceLog.Scope("TalkDynamicPatch.RemoveTranslationPatches");
        if (_harmony == null || !_applied)
        {
            TraceLog.Event($"TalkDynamicPatch.Remove.skip harmonyNull={_harmony == null} applied={_applied}");
            return;
        }

        foreach (var method in TranslationMethods)
        {
            _harmony.Unpatch(method, HarmonyPatchType.Prefix, HarmonyId);
            TraceLog.Event($"TalkDynamicPatch.Remove.unpatch {method.DeclaringType?.FullName}.{method.Name}");
        }

        _applied = false;
        TalkDiagnostics.HitDynamicRemove();
        LogAscii.LogInfo("  dynamic patch OFF");
    }

    private static HarmonyMethod PrefixFor(MethodBase method) => method.Name switch
    {
        "DisplayTalkScreen" => new HarmonyMethod(
            typeof(TextPatchHelper), nameof(TextPatchHelper.DisplayTalkScreenPrefix)),
        _ => new HarmonyMethod(typeof(TextPatchHelper), nameof(TextPatchHelper.ShowTalkPrefix)),
    };

    private static bool IsTranslationPatched(MethodBase method)
    {
        using var scope = TraceLog.Scope("TalkDynamicPatch.IsTranslationPatched");
        var info = Harmony.GetPatchInfo(method);
        return info?.Prefixes?.Any(p => p.owner == HarmonyId) == true;
    }
}

// --- HarmonyPatchGuard.cs ---
/// <summary>
/// Il2CPP pode expor o mesmo metodo como varios MethodInfo — evita patch/postfix duplicado.
/// </summary>
internal static class HarmonyPatchGuard
{
    private static readonly HashSet<string> ClaimedSignatures = new(StringComparer.Ordinal);

    internal static string Signature(MethodBase method)
    {
        var parameters = method.GetParameters();
        if (parameters.Length == 0)
            return $"{method.DeclaringType?.FullName}.{method.Name}()";

        var types = string.Join(",", parameters.Select(p => p.ParameterType.FullName ?? p.ParameterType.Name));
        return $"{method.DeclaringType?.FullName}.{method.Name}({types})";
    }

    internal static bool TryClaim(MethodBase method) => ClaimedSignatures.Add(Signature(method));

    internal static bool PatchPostfix(Harmony harmony, MethodBase method, HarmonyMethod postfix)
    {
        if (!TryClaim(method))
            return false;

        harmony.Patch(method, postfix: postfix);
        return true;
    }

    internal static bool PatchPrefix(Harmony harmony, MethodBase method, HarmonyMethod prefix)
    {
        if (!TryClaim(method))
            return false;

        harmony.Patch(method, prefix: prefix);
        return true;
    }

    internal static bool PatchPrefixPostfix(
        Harmony harmony,
        MethodBase method,
        HarmonyMethod? prefix = null,
        HarmonyMethod? postfix = null)
    {
        if (!TryClaim(method))
            return false;

        harmony.Patch(method, prefix: prefix, postfix: postfix);
        return true;
    }

    internal static bool SameSignature(MethodBase a, MethodBase b) =>
        Signature(a) == Signature(b);
}

// --- TalkScreenPatch.cs ---
internal static class TalkScreenPatch
{
    private const string ControllerTypeName = "UIInteractionController";

    private static readonly string[] ShowTalkNames = { "ShowTalk" };
    private static readonly string[] DisplayTalkNames = { "DisplayTalkScreen" };
    private static readonly string[] TranslationHookNames = { "ProcessTalkScreen" };
    private static readonly string[] SessionOpenNames = { "ProcessPreTalkScreen" };
    private static readonly string[] SessionNpcNames = { "ProcessStartInteraction" };
    private static readonly string[] SessionSelectEntityNames = { "ProcessSelectEntity" };
    private static readonly string[] SessionCloseNames = { "ProcessEndInteraction" };
    private static readonly string[] ProbeNames =
    {
        "ShowTalk",
        "ProcessTalkScreen",
        "ProcessPreTalkScreen",
        "ProcessStartInteraction",
        "ProcessSelectEntity",
        "ProcessEndInteraction",
    };

    internal static int PatchAll(Harmony harmony)
    {
        TraceLog.Event("PatchAll.begin");
        var controllerType = FindControllerType();
        if (controllerType == null)
        {
            LogAscii.LogWarning("UIInteractionController nao encontrado — nenhum patch aplicado.");
            TraceLog.Event("PatchAll.controllerType.missing");
            return 0;
        }

        TraceLog.Event($"PatchAll.controllerType={controllerType.FullName}");
        UiInteractionFinder.Init(controllerType);

        Dictionary<string, List<MethodInfo>> controllerMethods;
        using (TraceLog.Scope("PatchAll.IndexControllerMethods"))
            controllerMethods = IndexMethods(controllerType);

        Dictionary<string, List<MethodInfo>> globalMethods;
        using (TraceLog.Scope("PatchAll.IndexMethodsGlobally"))
            globalMethods = IndexMethodsGlobally(ProbeNames);

        Dictionary<string, List<MethodInfo>> mergedMethods;
        using (TraceLog.Scope("PatchAll.MergeMaps"))
            mergedMethods = MergeMaps(controllerMethods, globalMethods);

        var count = 0;

        if (PluginSettings.DiscoveryMode || PluginSettings.DiagnosticMode)
        {
            if (PluginSettings.DiagnosticMode)
            {
                using (TraceLog.Scope("PatchAll.PatchProbes"))
                    count += PatchProbes(harmony, mergedMethods);

                var onPreTalkProbe = new HarmonyMethod(typeof(TextPatchHelper), nameof(TextPatchHelper.ProbeOnPreTalkPostfix));
                var fancyMenuProbe = new HarmonyMethod(typeof(TextPatchHelper), nameof(TextPatchHelper.ProbeUpdateFancyMenuPostfix));
                var fancyMenuOptionsProbe = new HarmonyMethod(typeof(TextPatchHelper), nameof(TextPatchHelper.ProbeUpdateFancyMenuOptionsPostfix));

                using (TraceLog.Scope("PatchAll.PatchInteractionProbes"))
                {
                    count += PatchControllerPostfixes(harmony, controllerType, "OnPreTalk", onPreTalkProbe);
                    count += PatchControllerPostfixes(harmony, controllerType, "UpdateFancyMenu", fancyMenuProbe);
                    count += PatchControllerPostfixes(harmony, controllerType, "UpdateFancyMenuOptions", fancyMenuOptionsProbe);
                }

                TalkDiagnostics.Start(PluginSettings.DiagnosticIntervalSec);
                LogAscii.LogInfo("  diagnostic ON -> BepInEx/plugins/PgTranslateLive/hook-stats.log");
            }

            if (PluginSettings.DiscoveryMode)
            {
                TalkDiscovery.Start(PluginSettings.DiscoveryIntervalSec);

                using (TraceLog.Scope("PatchAll.PatchDiscovery"))
                    count += TalkDiscovery.PatchAll(harmony);

                LogAscii.LogInfo(
                    $"  discovery ON -> BepInEx/plugins/PgTranslateLive/discovery.log ({TalkDiscovery.PatchedMethods} metodos)");
            }

            TraceLog.Event($"PatchAll.end probe patches={count}");
            return count;
        }

        var showTalkMethods = ResolveMethods(controllerMethods, ShowTalkNames).ToList();
        var displayTalkMethods = ResolveMethods(controllerMethods, DisplayTalkNames).ToList();
        TraceLog.Event($"PatchAll.ShowTalkMethods={showTalkMethods.Count} DisplayTalkScreen={displayTalkMethods.Count}");
        TalkDynamicPatch.Init(harmony, showTalkMethods);

        switch (PluginSettings.Strategy)
        {
            case PatchStrategy.DynamicShowTalk:
                using (TraceLog.Scope("PatchAll.PatchSessionHooks"))
                    count += PatchSessionHooks(harmony, globalMethods, controllerType);
                // DisplayTalkScreen pinta o texto na UI — patch permanente (so roda no dialogo).
                using (TraceLog.Scope("PatchAll.PatchDisplayTalkScreenAlways"))
                {
                    count += PatchTranslationHooks(
                        harmony,
                        displayTalkMethods,
                        nameof(TextPatchHelper.DisplayTalkScreenPrefix));
                }
                if (count == 0)
                {
                    LogAscii.LogWarning(
                        "ProcessPreTalkScreen/ProcessEndInteraction nao encontrados — fallback AlwaysShowTalk.");
                    using (TraceLog.Scope("PatchAll.FallbackAlwaysShowTalk"))
                    {
                        count += PatchTranslationHooks(
                            harmony,
                            showTalkMethods,
                            nameof(TextPatchHelper.ShowTalkPrefix));
                    }
                }
                else
                {
                    LogAscii.LogInfo(
                        $"  strategy DynamicShowTalk ({showTalkMethods.Count} ShowTalk deferred + {displayTalkMethods.Count} DisplayTalkScreen always)");
                }
                break;

            case PatchStrategy.ProcessTalkScreen:
                using (TraceLog.Scope("PatchAll.PatchProcessTalkScreen"))
                {
                    count += PatchTranslationHooks(
                        harmony,
                        globalMethods,
                        TranslationHookNames,
                        nameof(TextPatchHelper.ProcessTalkScreenPrefix));
                }
                LogAscii.LogInfo("  strategy ProcessTalkScreen");
                break;

            case PatchStrategy.AlwaysShowTalk:
                using (TraceLog.Scope("PatchAll.PatchAlwaysShowTalk"))
                {
                    count += PatchTranslationHooks(
                        harmony,
                        showTalkMethods,
                        nameof(TextPatchHelper.ShowTalkPrefix));
                    count += PatchTranslationHooks(
                        harmony,
                        displayTalkMethods,
                        nameof(TextPatchHelper.DisplayTalkScreenPrefix));
                }
                LogAscii.LogInfo(
                    $"  strategy AlwaysShowTalk ({showTalkMethods.Count} ShowTalk + {displayTalkMethods.Count} DisplayTalkScreen)");
                break;
        }

        TraceLog.Event($"PatchAll.end strategy={PluginSettings.Strategy} patches={count}");
        return count;
    }

    private static Type? FindControllerType()
    {
        using var scope = TraceLog.Scope("FindControllerType");
        var type = AccessTools.TypeByName(ControllerTypeName);
        if (type != null)
        {
            TraceLog.Event("FindControllerType.TypeByName.hit");
            return type;
        }

        var scanned = 0;
        foreach (var t in AccessTools.AllTypes())
        {
            scanned++;
            if (t.Name == ControllerTypeName)
            {
                TraceLog.Counter("FindControllerType.AllTypes.scanned", scanned);
                TraceLog.Event($"FindControllerType.AllTypes.hit scanned={scanned}");
                return t;
            }
        }

        TraceLog.Counter("FindControllerType.AllTypes.scanned", scanned);
        TraceLog.Event($"FindControllerType.miss scanned={scanned}");
        return null;
    }

    private static Dictionary<string, List<MethodInfo>> IndexMethods(Type type)
    {
        var map = new Dictionary<string, List<MethodInfo>>(StringComparer.Ordinal);
        var methodCount = 0;
        foreach (var method in AccessTools.GetDeclaredMethods(type))
        {
            AddMethod(map, method);
            methodCount++;
        }

        TraceLog.Event($"IndexMethods type={type.FullName} methods={methodCount}");
        return map;
    }

    /// <summary>
    /// ProcessPreTalkScreen / ProcessEndInteraction ficam em outra classe (nao UIInteractionController).
    /// </summary>
    private static Dictionary<string, List<MethodInfo>> IndexMethodsGlobally(IEnumerable<string> names)
    {
        var wanted = new HashSet<string>(names, StringComparer.Ordinal);
        var map = new Dictionary<string, List<MethodInfo>>(StringComparer.Ordinal);
        var typeCount = 0;
        var methodCount = 0;
        var matchCount = 0;

        foreach (var type in AccessTools.AllTypes())
        {
            typeCount++;
            if (type.IsInterface)
                continue;

            foreach (var method in AccessTools.GetDeclaredMethods(type))
            {
                methodCount++;
                if (!wanted.Contains(method.Name))
                    continue;
                if (method.IsAbstract)
                    continue;

                AddMethod(map, method);
                matchCount++;
                TraceLog.Event($"IndexMethodsGlobally.match {type.FullName}.{method.Name}");
            }
        }

        TraceLog.Counter("IndexMethodsGlobally.types", typeCount);
        TraceLog.Counter("IndexMethodsGlobally.methods", methodCount);
        TraceLog.Counter("IndexMethodsGlobally.matches", matchCount);
        TraceLog.Event($"IndexMethodsGlobally.done types={typeCount} methods={methodCount} matches={matchCount}");
        return map;
    }

    private static Dictionary<string, List<MethodInfo>> MergeMaps(
        Dictionary<string, List<MethodInfo>> primary,
        Dictionary<string, List<MethodInfo>> secondary)
    {
        var merged = new Dictionary<string, List<MethodInfo>>(primary, StringComparer.Ordinal);
        foreach (var (name, list) in secondary)
        {
            if (!merged.TryGetValue(name, out var target))
            {
                merged[name] = list.ToList();
                continue;
            }

            foreach (var method in list)
            {
                if (target.Any(m => HarmonyPatchGuard.SameSignature(m, method)))
                    continue;

                target.Add(method);
            }
        }

        return merged;
    }

    private static void AddMethod(Dictionary<string, List<MethodInfo>> map, MethodInfo method)
    {
        if (!map.TryGetValue(method.Name, out var list))
        {
            list = new List<MethodInfo>();
            map[method.Name] = list;
        }

        if (list.Any(m => HarmonyPatchGuard.SameSignature(m, method)))
            return;

        list.Add(method);
    }

    private static IEnumerable<MethodInfo> ResolveMethods(
        Dictionary<string, List<MethodInfo>> map,
        IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (!map.TryGetValue(name, out var list))
                continue;

            foreach (var method in list)
                yield return method;
        }
    }

    private static int PatchSessionHooks(Harmony harmony, Dictionary<string, List<MethodInfo>> map, Type controllerType)
    {
        var count = 0;
        var npcCapture = new HarmonyMethod(typeof(TextPatchHelper), nameof(TextPatchHelper.ProcessStartInteractionPrefix));
        var npcCapturePost = new HarmonyMethod(typeof(TextPatchHelper), nameof(TextPatchHelper.ProcessStartInteractionPostfix));
        var selectEntity = new HarmonyMethod(typeof(TextPatchHelper), nameof(TextPatchHelper.ProcessSelectEntityPrefix));
        var selectEntityPost = new HarmonyMethod(typeof(TextPatchHelper), nameof(TextPatchHelper.ProcessSelectEntityPostfix));
        var open = new HarmonyMethod(typeof(TextPatchHelper), nameof(TextPatchHelper.SessionOpenPrefix));
        var close = new HarmonyMethod(typeof(TextPatchHelper), nameof(TextPatchHelper.SessionClosePostfix));
        var onPreTalk = new HarmonyMethod(typeof(TextPatchHelper), nameof(TextPatchHelper.OnPreTalkPostfix));

        foreach (var method in ResolveMethods(map, SessionNpcNames))
        {
            if (!HarmonyPatchGuard.PatchPrefixPostfix(harmony, method, prefix: npcCapture, postfix: npcCapturePost))
                continue;

            TraceLog.Counter("Harmony.Patch.sessionNpc");
            TraceLog.Event($"Harmony.Patch.sessionNpc {method.DeclaringType?.FullName}.{method.Name}");
            count++;
            LogAscii.LogInfo($"  session npc {method.DeclaringType?.Name}.{method.Name}");
        }

        foreach (var method in ResolveMethods(map, SessionSelectEntityNames))
        {
            if (!HarmonyPatchGuard.PatchPrefixPostfix(harmony, method, prefix: selectEntity, postfix: selectEntityPost))
                continue;

            TraceLog.Counter("Harmony.Patch.sessionSelectEntity");
            TraceLog.Event($"Harmony.Patch.sessionSelectEntity {method.DeclaringType?.FullName}.{method.Name}");
            count++;
            LogAscii.LogInfo($"  session select {method.DeclaringType?.Name}.{method.Name}");
        }

        foreach (var method in ResolveMethods(map, SessionOpenNames))
        {
            if (!HarmonyPatchGuard.PatchPrefix(harmony, method, open))
                continue;

            TraceLog.Counter("Harmony.Patch.sessionOpen");
            TraceLog.Event($"Harmony.Patch.sessionOpen {method.DeclaringType?.FullName}.{method.Name}");
            count++;
            LogAscii.LogInfo($"  session open {method.DeclaringType?.Name}.{method.Name}");
        }

        count += PatchControllerPostfixes(harmony, controllerType, "OnPreTalk", onPreTalk);
        count += InteractionClickPatch.PatchAll(harmony, controllerType);

        foreach (var method in ResolveMethods(map, SessionCloseNames))
        {
            if (!HarmonyPatchGuard.PatchPostfix(harmony, method, close))
                continue;

            TraceLog.Counter("Harmony.Patch.sessionClose");
            TraceLog.Event($"Harmony.Patch.sessionClose {method.DeclaringType?.FullName}.{method.Name}");
            count++;
            LogAscii.LogInfo($"  session close {method.DeclaringType?.Name}.{method.Name}");
        }

        return count;
    }

    private static int PatchControllerPostfixes(
        Harmony harmony,
        Type controllerType,
        string methodName,
        HarmonyMethod postfix)
    {
        var count = 0;

        foreach (var method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (method.Name != methodName || method.IsAbstract)
                continue;

            if (!HarmonyPatchGuard.PatchPostfix(harmony, method, postfix))
                continue;

            TraceLog.Counter($"Harmony.Patch.{methodName}");
            TraceLog.Event($"Harmony.Patch.{methodName} {method.DeclaringType?.FullName}.{method.Name}");
            count++;
            LogAscii.LogInfo($"  {methodName} {method.DeclaringType?.Name}.{method.Name}");
        }

        return count;
    }

    private static int PatchTranslationHooks(
        Harmony harmony,
        Dictionary<string, List<MethodInfo>> map,
        IEnumerable<string> names,
        string prefixName) =>
        PatchTranslationHooks(harmony, ResolveMethods(map, names), prefixName);

    private static int PatchTranslationHooks(
        Harmony harmony,
        IEnumerable<MethodInfo> methods,
        string prefixName)
    {
        var prefix = new HarmonyMethod(typeof(TextPatchHelper), prefixName);
        var count = 0;

        foreach (var method in methods)
        {
            if (!HarmonyPatchGuard.PatchPrefix(harmony, method, prefix))
                continue;

            TraceLog.Counter("Harmony.Patch.translation");
            TraceLog.Event($"Harmony.Patch.translation prefix={prefixName} method={method.DeclaringType?.FullName}.{method.Name}");
            count++;
            LogAscii.LogInfo($"  patch {method.DeclaringType?.Name}.{method.Name}");
        }

        return count;
    }

    private static int PatchProbes(Harmony harmony, Dictionary<string, List<MethodInfo>> map)
    {
        var count = 0;

        foreach (var name in ProbeNames)
        {
            if (!map.TryGetValue(name, out var list))
                continue;

            foreach (var method in list)
            {
                if (name == "ProcessEndInteraction")
                {
                    var postfix = new HarmonyMethod(typeof(TextPatchHelper), ProbePostfixName(name));
                    if (!HarmonyPatchGuard.PatchPostfix(harmony, method, postfix))
                        continue;
                    TraceLog.Counter("Harmony.Patch.probePostfix");
                }
                else
                {
                    var prefix = new HarmonyMethod(typeof(TextPatchHelper), ProbePrefixName(name));
                    if (!HarmonyPatchGuard.PatchPrefix(harmony, method, prefix))
                        continue;
                    TraceLog.Counter("Harmony.Patch.probePrefix");
                }

                count++;
                TraceLog.Event($"Harmony.Patch.probe {method.DeclaringType?.FullName}.{method.Name}");
                LogAscii.LogInfo($"  probe {method.DeclaringType?.Name}.{method.Name}");
            }
        }

        return count;
    }

    private static string ProbePrefixName(string methodName) => methodName switch
    {
        "ShowTalk" => nameof(TextPatchHelper.ProbeShowTalkPrefix),
        "ProcessTalkScreen" => nameof(TextPatchHelper.ProbeProcessTalkScreenPrefix),
        "ProcessPreTalkScreen" => nameof(TextPatchHelper.ProbeProcessPreTalkScreenPrefix),
        "ProcessStartInteraction" => nameof(TextPatchHelper.ProbeProcessStartInteractionPrefix),
        "ProcessSelectEntity" => nameof(TextPatchHelper.ProbeProcessSelectEntityPrefix),
        _ => nameof(TextPatchHelper.ProbeShowTalkPrefix),
    };

    private static string ProbePostfixName(string methodName) => methodName switch
    {
        "ProcessEndInteraction" => nameof(TextPatchHelper.ProbeProcessEndInteractionPostfix),
        _ => nameof(TextPatchHelper.ProbeProcessEndInteractionPostfix),
    };
}

// --- TalkSession.cs ---
// LogPolicy.Reminder — FinishInteractionCapture, LogInteraction, CheckNpcInJson sao protegidos.
internal static class TalkSession
{
    internal static bool Active { get; private set; }
    internal static string NpcId { get; private set; } = "";
    internal static string NpcName { get; private set; } = "";
    internal static string NpcSubtext { get; private set; } = "";
    private static bool _npcRegistered;
    private static bool _interactionLogged;
    private static bool _googleUsedThisDialog;
    private static bool _npcRegistrySaveAttempted;
    private static int _selectedEntityId = -1;
    private static object? _lastSelection;
    private static string _lastLogKey = "";
    private static long _lastLogTicks;

    internal static bool InteractionLogged => _interactionLogged;
    internal static bool GoogleUsedThisDialog => _googleUsedThisDialog;
    internal static int SelectedEntityId => _selectedEntityId;
    internal static object? LastSelection => _lastSelection;

    /// <summary>Limpa estado do NPC anterior — cada clique comeca do zero.</summary>
    internal static void BeginInteractionCapture()
    {
        if (Active)
            return;

        NpcId = "";
        NpcName = "";
        NpcSubtext = "";
        _npcRegistered = false;
        _interactionLogged = false;
    }

    internal static void SetNpc(string npcId)
    {
        NpcId = npcId?.Trim() ?? "";
        NpcName = NpcNameResolver.Resolve(NpcId);
        NpcSubtext = "";
        TraceLog.Event($"TalkSession.SetNpc id={NpcId} name={NpcName}");
    }

    internal static bool PrimeFromPack()
    {
        if (string.IsNullOrWhiteSpace(NpcId))
            return false;

        if (!NpcRegistryStore.TryGetNpcContext(NpcId, out var name, out var desc, out _))
            return false;

        if (!string.IsNullOrWhiteSpace(name))
            SetDisplayName(name);
        if (!string.IsNullOrWhiteSpace(desc))
            SetSubtext(desc);

        return !string.IsNullOrWhiteSpace(desc);
    }

    internal static void SetLastSelection(object? selection)
    {
        _lastSelection = selection;
    }

    internal static void SetSelectedEntity(int entityId)
    {
        _selectedEntityId = entityId;
        TraceLog.Event($"TalkSession.SetSelectedEntity id={entityId}");
    }

    internal static void FinishInteractionCapture(string? fallbackLabel = null, object? controllerHint = null)
    {
        // LogPolicy.RulePath — nao remover TryCaptureFromClick nem LogInteraction/CheckNpcInJson.
        if (Active)
            return;

        InteractionSubtextCapture.TryCaptureFromClick("FinishInteractionCapture", controllerHint);

        var label = BuildInteractionLabel(fallbackLabel);
        if (label == null)
            return;

        var key = $"{_selectedEntityId}|{NpcId}|{NpcName}|{label}";
        var now = DateTime.UtcNow.Ticks;
        if (key == _lastLogKey && now - _lastLogTicks < TimeSpan.TicksPerMillisecond * 250)
            return;

        _lastLogKey = key;
        _lastLogTicks = now;

        LogInteraction(label);
        CheckNpcInJson();
    }

    private static void CheckNpcInJson()
    {
        // LogPolicy.RulePath — Consultar npc json + npc ja existe; nao remover.
        if (string.IsNullOrWhiteSpace(NpcId) && string.IsNullOrWhiteSpace(NpcName))
            return;

        var name = string.IsNullOrWhiteSpace(NpcName)
            ? NpcNameResolver.Resolve(NpcId)
            : NpcName;

        var id = NpcRegistryStore.ResolveNpcId(NpcId, name);
        if (!string.IsNullOrWhiteSpace(id))
            EnsureNpcId(id);

        TalkLog.NpcJsonChecking();

        if (!string.IsNullOrWhiteSpace(id)
            && NpcRegistryStore.TryGetNpcContext(id, out var foundName, out var foundDesc, out var sourceFile))
        {
            TalkLog.NpcJsonFound(
                string.IsNullOrWhiteSpace(foundName) ? name : foundName,
                foundDesc,
                sourceFile);
            return;
        }

        TalkLog.NpcJsonMissing(name, NpcSubtext);

        if (!NpcRegistryStore.SaveNew || string.IsNullOrWhiteSpace(id))
            return;

        if (NpcRegistryStore.SaveOnInteraction(id, name, NpcSubtext))
            TalkLog.NpcJsonSaved(name, id, NpcRegistryStore.FileName);
    }

    private static string? BuildInteractionLabel(string? fallbackLabel)
    {
        if (!string.IsNullOrEmpty(NpcId))
            return NpcId;
        if (!string.IsNullOrWhiteSpace(NpcName))
            return NpcName;
        if (_selectedEntityId > 0)
            return $"entity:{_selectedEntityId}";
        return string.IsNullOrWhiteSpace(fallbackLabel) ? null : fallbackLabel;
    }

    private static void LogInteraction(string label)
    {
        // LogPolicy.RulePath — etapa Interacao + subtexto inline; nao remover.
        _interactionLogged = true;
        var detail = $"jogador clicou no npc {label}";
        if (!string.IsNullOrWhiteSpace(NpcSubtext))
            detail += $" | subtexto: {NpcSubtext}";

        TalkLog.Interacao(detail);
    }

    internal static void ResetForNewTalk() => _npcRegistrySaveAttempted = false;

    internal static void EnsureInteractionLogged()
    {
        if (_interactionLogged)
            return;

        var label = BuildInteractionLabel(null);
        if (label == null)
            return;

        LogInteraction(label);
    }

    internal static void ConsultNpcRegistry() => CheckNpcInJson();

    internal static void MarkGoogleUsed() => _googleUsedThisDialog = true;

    internal static void EnsureNpcId(string npcId)
    {
        if (string.IsNullOrWhiteSpace(npcId) || !string.IsNullOrEmpty(NpcId))
            return;

        NpcId = npcId.Trim();
        if (string.IsNullOrWhiteSpace(NpcName))
            NpcName = NpcNameResolver.Resolve(NpcId);
        TraceLog.Event($"TalkSession.EnsureNpcId id={NpcId}");
    }

    /// <summary>Subtexto chegou depois do log Interacao (ex.: UpdateFancyMenu) — atualiza registry.</summary>
    internal static void NotifySubtextCaptured()
    {
        if (!_interactionLogged)
            return;

        var name = string.IsNullOrWhiteSpace(NpcName) ? NpcNameResolver.Resolve(NpcId) : NpcName;
        var id = NpcRegistryStore.ResolveNpcId(NpcId, name);
        if (string.IsNullOrWhiteSpace(id))
            return;

        EnsureNpcId(id);
        if (NpcRegistryStore.SaveOnInteraction(id, name, NpcSubtext))
            TalkLog.NpcJsonSaved(name, id, NpcRegistryStore.FileName);
    }

    internal static string NormalizeInteractorLabel(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        var text = raw.Trim();
        const string prefix = "Nameplate:";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            text = text[prefix.Length..].Trim();

        return text;
    }

    internal static void SetDisplayName(string name)
    {
        var cleaned = NormalizeInteractorLabel(name);
        if (string.IsNullOrWhiteSpace(cleaned))
            return;

        if (!cleaned.Equals(NpcName, StringComparison.OrdinalIgnoreCase))
        {
            NpcId = "";
            NpcSubtext = "";
        }

        NpcName = cleaned;
        TraceLog.Event($"TalkSession.SetDisplayName name={NpcName}");
    }

    internal static void SetSubtext(string subtext)
    {
        if (string.IsNullOrWhiteSpace(subtext))
            return;

        NpcSubtext = subtext.Trim();
        TraceLog.Event($"TalkSession.SetSubtext desc={TalkLog.Preview(NpcSubtext)}");
    }

    internal static void Open()
    {
        Active = true;
        _npcRegistered = false;
        _googleUsedThisDialog = false;
        TalkDiagnostics.HitSessionOpen();

        if (!string.IsNullOrEmpty(NpcName))
            TalkLog.Pipeline(TalkPipelineStep.Falar, $"dialogo com {NpcName}");
    }

    internal static void RegisterNpc()
    {
        RegisterNpcIfReady();
    }

    internal static void RegisterNpcIfReady()
    {
        if (_npcRegistered)
            return;

        _npcRegistered = true;
        NpcRegistryStore.EnsureOnTalk(NpcId, NpcName, NpcSubtext);
    }

    internal static void Close()
    {
        Active = false;
        _npcRegistered = false;
        _interactionLogged = false;
        _googleUsedThisDialog = false;
        _npcRegistrySaveAttempted = false;
        _selectedEntityId = -1;
        _lastSelection = null;
        _lastLogKey = "";
        _lastLogTicks = 0;
        NpcId = "";
        NpcName = "";
        NpcSubtext = "";
        UiInteractionFinder.ClearCache();
        TalkDiagnostics.HitSessionClose();
    }
}
// --- TalkTurn.cs ---
/// <summary>
/// Cache da conversa atual — ShowTalk grava, DisplayTalkScreen aplica na UI.
/// </summary>
internal static class TalkTurn
{
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    internal static bool HasTurn => !Cache.IsEmpty;

    internal static void Clear()
    {
        Cache.Clear();
        TraceLog.Counter("TalkTurn.Clear");
    }

    internal static void Store(string source, string translated)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(translated))
            return;

        Cache[source] = translated;
        TraceLog.Counter("TalkTurn.Store");
    }

    internal static bool TryGet(string source, out string translated)
    {
        if (Cache.TryGetValue(source, out var hit) && !string.IsNullOrEmpty(hit))
        {
            translated = hit;
            TraceLog.Counter("TalkTurn.TryGet.hit");
            return true;
        }

        translated = null!;
        TraceLog.Counter("TalkTurn.TryGet.miss");
        return false;
    }

    internal static string[] Apply(string[] sources, TextKind[] kinds)
    {
        var results = new string[sources.Length];
        for (var i = 0; i < sources.Length; i++)
        {
            var source = sources[i];
            if (TryGet(source, out var hit) && hit != source)
                results[i] = hit;
            else if (NpcTalkStore.TryGet(source, out var yaml) && yaml != source)
                results[i] = yaml;
        }

        return results;
    }
}

// --- TextPatchHelper.cs ---
/// <summary>
/// Traduz em ShowTalk / ProcessTalkScreen — patch dinâmico evita hook no mapa.
/// </summary>
internal static class TextPatchHelper
{
    private sealed class ChoiceTarget
    {
        internal object Item = null!;
        internal Type ItemType = null!;
    }

    private sealed class StringArraySlot
    {
        internal object Array = null!;
        internal int Index;
    }

    [ThreadStatic]
    private static bool _guard;

    // --- Diagnostic probes (sem tradução, só contador) ---

    public static void ProbeShowTalkPrefix()
    {
        TraceLog.Hook("probe.ShowTalk");
        TalkDiagnostics.HitShowTalk();
    }

    public static void ProbeProcessTalkScreenPrefix()
    {
        TraceLog.Hook("probe.ProcessTalkScreen");
        TalkDiagnostics.HitProcessTalkScreen();
    }

    public static void ProbeProcessPreTalkScreenPrefix(object[] __args)
    {
        TraceLog.Hook("probe.ProcessPreTalkScreen");
        TalkDiagnostics.HitProcessPreTalkScreen();
        NpcIdDebugProbe.ScanHook("ProcessPreTalkScreen", __args);
        PreTalkScreenProbe.CaptureFromArgs(__args);
    }

    public static void ProbeProcessStartInteractionPrefix(object[] __args)
    {
        TraceLog.Hook("probe.ProcessStartInteraction");
        TalkDiagnostics.HitProcessStartInteraction();
        NpcIdDebugProbe.ScanHook("ProcessStartInteraction", __args);
        CaptureNpcFromArgs(__args);
    }

    public static void ProcessStartInteractionPrefix(object[] __args)
    {
        TraceLog.Hook("ProcessStartInteractionPrefix");
        TalkDiagnostics.HitProcessStartInteraction();
        NpcIdDebugProbe.ScanHook("ProcessStartInteraction", __args);
        CaptureNpcFromArgs(__args);
        TalkSession.ResetForNewTalk();
        PreTalkScreenProbe.PrimeSubtextFromController();
        TalkSession.EnsureInteractionLogged();
        TalkSession.ConsultNpcRegistry();
    }

    public static void ProcessStartInteractionPostfix()
    {
        TraceLog.Hook("ProcessStartInteractionPostfix");
        var controller = UiInteractionFinder.GetController();
        PreTalkScreenProbe.CaptureFromInteractionPanel(controller, logInteraction: false);
    }

    public static void ProbeProcessSelectEntityPrefix(int entityId)
    {
        TraceLog.Hook("probe.ProcessSelectEntity");
        TalkDiagnostics.HitProcessSelectEntity();
        TalkSession.SetSelectedEntity(entityId);
    }

    public static void ProcessSelectEntityPrefix(int entityId)
    {
        TraceLog.Hook("ProcessSelectEntityPrefix");
        TalkDiagnostics.HitProcessSelectEntity();
        TalkSession.SetSelectedEntity(entityId);
    }

    public static void ProcessSelectEntityPostfix()
    {
        TraceLog.Hook("ProcessSelectEntityPostfix");
        var controller = UiInteractionFinder.GetController();
        PreTalkScreenProbe.CaptureFromInteractionPanel(controller, logInteraction: false);
        TalkSession.FinishInteractionCapture();
    }

    // Desligado (nao patchado) — ver InteractionClickPatch.PatchAll.
    public static void UiSelectionPostfix(object __instance, object __0)
    {
        TraceLog.Hook("UiSelectionPostfix");
        TalkDiagnostics.HitUiSelection();
        var controller = UiInteractionFinder.Resolve(__instance);
        PreTalkScreenProbe.CaptureFromSelection(__0);
        TalkSession.FinishInteractionCapture(__0?.GetType().Name, controller);
    }

    public static void UpdateInteractorNameStringPostfix(object __instance, object __0)
    {
        TraceLog.Hook("UpdateInteractorNameStringPostfix");
        TalkDiagnostics.HitUpdateInteractorName();
        UiInteractionFinder.Remember(__instance);
        ApplyInteractorName(__instance, -1, Il2CppStringHelper.Read(__0));
    }

    public static void UpdateInteractorNameEntityStringPostfix(object __instance, int __0, object __1)
    {
        TraceLog.Hook("UpdateInteractorNameEntityStringPostfix");
        TalkDiagnostics.HitUpdateInteractorName();
        UiInteractionFinder.Remember(__instance);
        ApplyInteractorName(__instance, __0, Il2CppStringHelper.Read(__1));
    }

    private static void ApplyInteractorName(object? hookInstance, int entityId, string? name)
    {
        if (TalkSession.Active)
            return;

        if (entityId > 0)
            TalkSession.SetSelectedEntity(entityId);

        var cleaned = TalkSession.NormalizeInteractorLabel(name);
        if (!string.IsNullOrWhiteSpace(cleaned)
            && cleaned.Length <= 64
            && !cleaned.Equals("Talk", StringComparison.OrdinalIgnoreCase))
        {
            TalkSession.SetDisplayName(cleaned);
            if (string.IsNullOrEmpty(TalkSession.NpcId)
                && EntityNpcIdCache.TryGetByName(cleaned, out var cachedId))
            {
                TalkSession.SetNpc(cachedId);
                TalkLog.DebugNpcId($"cache hit name=\"{cleaned}\" id={cachedId}");
            }
        }

        InteractionSubtextCapture.TryCaptureFromClick("UpdateInteractorName", hookInstance);
        TalkSession.FinishInteractionCapture();
    }

    public static void ProbeProcessEndInteractionPostfix()
    {
        TraceLog.Hook("probe.ProcessEndInteraction");
        TalkDiagnostics.HitProcessEndInteraction();
    }

    public static void ProbeOnPreTalkPostfix(object __instance, object info)
    {
        TraceLog.Hook("probe.OnPreTalk");
        TalkDiagnostics.HitOnPreTalk();
        NpcIdDebugProbe.ScanHook("OnPreTalk", null, info);
    }

    public static void ProbeUpdateFancyMenuPostfix(object __instance)
    {
        TraceLog.Hook("probe.UpdateFancyMenu");
        TalkDiagnostics.HitUpdateFancyMenu();
    }

    public static void ProbeUpdateFancyMenuOptionsPostfix(object __instance)
    {
        TraceLog.Hook("probe.UpdateFancyMenuOptions");
        TalkDiagnostics.HitUpdateFancyMenuOptions();
    }

    public static void DiscoveryProbePrefix(MethodBase __originalMethod)
    {
        TalkDiscovery.Hit(__originalMethod);
    }

    // --- Sessão de diálogo (patch dinâmico) ---

    private static void CaptureNpcFromArgs(object[]? args)
    {
        if (args == null || args.Length == 0)
            return;

        for (var i = args.Length - 1; i >= 0; i--)
        {
            var value = Il2CppStringHelper.Read(args[i]);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (!NpcIdPatterns.LooksLikeNpcId(value) && !value.Contains('_', StringComparison.Ordinal))
                continue;

            if (NpcIdPatterns.LooksLikeNpcId(value))
                EntityNpcIdCache.Remember(TalkSession.SelectedEntityId, TalkSession.NpcName, value);

            TalkSession.SetNpc(value);
            return;
        }
    }

    public static void SessionOpenPrefix(object[] __args)
    {
        TraceLog.Hook("SessionOpenPrefix");
        using (TraceLog.Scope("SessionOpenPrefix"))
        {
            NpcIdDebugProbe.ScanHook("ProcessPreTalkScreen.session", __args);
            PreTalkScreenProbe.CaptureFromProcessPreTalk(__args);
            TalkSession.EnsureInteractionLogged();
            TalkSession.ConsultNpcRegistry();
            TalkTurn.Clear();
            TalkSession.Open();
            if (TranslateClient.Enabled && PluginSettings.Strategy == PatchStrategy.DynamicShowTalk)
                TalkDynamicPatch.ApplyTranslationPatches();
        }
    }

    public static void OnPreTalkPostfix(object __instance, object info)
    {
        TraceLog.Hook("OnPreTalkPostfix");
        TalkDiagnostics.HitOnPreTalk();
        UiInteractionFinder.Remember(__instance);
        NpcIdDebugProbe.ScanHook("OnPreTalk", null, info);
        using (TraceLog.Scope("OnPreTalkPostfix"))
            PreTalkScreenProbe.CaptureFromOnPreTalk(__instance, info);
    }

    public static void SessionClosePostfix()
    {
        TraceLog.Hook("SessionClosePostfix");
        using (TraceLog.Scope("SessionClosePostfix"))
        {
            if (PluginSettings.Strategy == PatchStrategy.DynamicShowTalk)
                TalkDynamicPatch.RemoveTranslationPatches();
            TalkTurn.Clear();
            TalkSession.Close();
        }
    }

    public static void DisplayTalkScreenPrefix(ref string text, object dialogChoices, MethodBase __originalMethod)
    {
        TraceLog.Hook("DisplayTalkScreenPrefix");
        if (!TranslateClient.Enabled || _guard)
        {
            TraceLog.Counter("DisplayTalkScreenPrefix.skipped");
            return;
        }

        try
        {
            _guard = true;
            using (TraceLog.Scope("DisplayTalkScreenPrefix.TranslateTalkScreen"))
                TranslateTalkScreen(ref text, dialogChoices, LivePhase.DisplayTalkScreen);
        }
        catch (Exception ex)
        {
            TraceLog.Error("DisplayTalkScreenPrefix", ex);
            TalkLog.Warn($"Erro em DisplayTalkScreen: {ex.Message}");
        }
        finally
        {
            _guard = false;
        }
    }

    // --- Tradução ---

    public static void ShowTalkPrefix(object[] __args, MethodBase __originalMethod)
    {
        TraceLog.Hook("ShowTalkPrefix");
        if (!TranslateClient.Enabled || _guard || __args == null)
        {
            TraceLog.Counter("ShowTalkPrefix.skipped");
            return;
        }

        try
        {
            _guard = true;
            using (TraceLog.Scope("ShowTalkPrefix.TranslateTalkArgs"))
                TranslateTalkArgs(__args, LivePhase.ShowTalk);
        }
        catch (Exception ex)
        {
            TraceLog.Error("ShowTalkPrefix", ex);
            TalkLog.Warn($"Erro em ShowTalk: {ex.Message}");
        }
        finally
        {
            _guard = false;
        }
    }

    public static void ProcessTalkScreenPrefix(object[] __args, MethodBase __originalMethod)
    {
        TraceLog.Hook("ProcessTalkScreenPrefix");
        if (!TranslateClient.Enabled || _guard || __args == null)
        {
            TraceLog.Counter("ProcessTalkScreenPrefix.skipped");
            return;
        }

        try
        {
            _guard = true;
            using (TraceLog.Scope("ProcessTalkScreenPrefix.TranslateTalkArgs"))
                TranslateTalkArgs(__args, LivePhase.ShowTalk);
        }
        catch (Exception ex)
        {
            TraceLog.Error("ProcessTalkScreenPrefix", ex);
            TalkLog.Warn($"Erro em ProcessTalkScreen: {ex.Message}");
        }
        finally
        {
            _guard = false;
        }
    }

    private static void TranslateTalkScreen(
        ref string text,
        object dialogChoices,
        LivePhase phase,
        bool choicesOnly = false)
    {
        var texts = new List<string>();
        var kinds = new List<TextKind>();
        var choiceTargets = new List<ChoiceTarget>();

        var hasMain = !choicesOnly && !string.IsNullOrWhiteSpace(text);
        if (hasMain)
        {
            texts.Add(text);
            kinds.Add(TextKind.Dialogue);
        }

        CollectChoiceTexts(dialogChoices, texts, kinds, choiceTargets);
        TraceLog.Event($"TranslateTalkScreen phase={phase} texts={texts.Count} choices={choiceTargets.Count}");

        if (texts.Count == 0)
            return;

        string[] translated;
        if (phase != LivePhase.ShowTalk && (TalkTurn.HasTurn || NpcTalkStore.UseLookup))
        {
            using (TraceLog.Scope("TranslateTalkScreen.ApplyFromTurn"))
                translated = TranslateClient.ApplyFromTurn(texts.ToArray(), kinds.ToArray(), phase);
            if (hasMain && (translated.Length == 0 || string.IsNullOrEmpty(translated[0])))
            {
                using (TraceLog.Scope("TranslateTalkScreen.ApplyFromTurn.fallback"))
                    translated = TranslateClient.TryTranslateBatch(texts.ToArray(), kinds.ToArray(), phase);
            }
        }
        else
        {
            using (TraceLog.Scope("TranslateTalkScreen.TryTranslateBatch"))
                translated = TranslateClient.TryTranslateBatch(texts.ToArray(), kinds.ToArray(), phase);
        }

        if (translated.Length != texts.Count)
            return;

        var offset = 0;
        if (hasMain)
        {
            if (!string.IsNullOrEmpty(translated[0]))
            {
                text = translated[0];
                if (phase == LivePhase.DisplayTalkScreen)
                    TalkLog.ShowingTranslated(text);
            }

            offset = 1;
        }

        for (var i = 0; i < choiceTargets.Count; i++)
        {
            var result = translated[offset + i];
            if (string.IsNullOrEmpty(result))
                continue;

            WriteChoiceText(choiceTargets[i].Item, choiceTargets[i].ItemType, result);
        }
    }

    private static void TranslateTalkArgs(object[] args, LivePhase phase)
    {
        var texts = new List<string>();
        var kinds = new List<TextKind>();
        var arraySlots = new List<StringArraySlot>();

        using (TraceLog.Scope("TranslateTalkArgs.collect"))
        {
            AddStringArg(args, 2, texts, kinds);
            CollectStringArrayArg(args, 5, texts, kinds, arraySlots);
        }

        TraceLog.Counter("TranslateTalkArgs.texts", texts.Count);
        TraceLog.Event($"TranslateTalkArgs phase={phase} texts={texts.Count} choices={arraySlots.Count}");

        if (texts.Count == 0)
            return;

        string[] translated;
        using (TraceLog.Scope("TranslateTalkArgs.TryTranslateBatch"))
        {
            translated = TranslateClient.TryTranslateBatch(
                texts.ToArray(), kinds.ToArray(), phase);
        }

        using (TraceLog.Scope("TranslateTalkArgs.ApplyBatchResults"))
            ApplyBatchResults(kinds, translated, args, arraySlots);
    }

    private static void AddStringArg(
        object[] args,
        int index,
        List<string> texts,
        List<TextKind> kinds)
    {
        if (index < 0 || index >= args.Length)
            return;

        var value = Il2CppStringHelper.Read(args[index]);
        if (string.IsNullOrWhiteSpace(value))
            return;

        texts.Add(value);
        kinds.Add(TextKind.Dialogue);
    }

    private static void CollectStringArrayArg(
        object[] args,
        int index,
        List<string> texts,
        List<TextKind> kinds,
        List<StringArraySlot> slots)
    {
        if (index < 0 || index >= args.Length || args[index] == null)
            return;

        CollectStringArray(args[index], texts, kinds, slots);
    }

    private static void CollectStringArray(
        object arrayValue,
        List<string> texts,
        List<TextKind> kinds,
        List<StringArraySlot> slots)
    {
        if (arrayValue is string[] strings)
        {
            TraceLog.Event($"CollectStringArray managed length={strings.Length}");
            for (var i = 0; i < strings.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(strings[i]))
                    continue;

                slots.Add(new StringArraySlot { Array = arrayValue, Index = i });
                texts.Add(strings[i]);
                kinds.Add(TextKind.Choice);
            }

            return;
        }

        using var scope = TraceLog.Scope("CollectStringArray.il2cpp");
        Il2CppArrayReflection.EnsureInitialized(arrayValue);
        var length = Il2CppArrayReflection.GetLength(arrayValue);
        TraceLog.Event($"CollectStringArray il2cpp type={arrayValue.GetType().FullName} length={length}");
        for (var i = 0; i < length; i++)
        {
            var current = Il2CppArrayReflection.GetStringAt(arrayValue, i);
            if (string.IsNullOrWhiteSpace(current))
                continue;

            slots.Add(new StringArraySlot { Array = arrayValue, Index = i });
            texts.Add(current);
            kinds.Add(TextKind.Choice);
        }
    }

    private static void ApplyBatchResults(
        List<TextKind> kinds,
        string[] translated,
        object[] args,
        List<StringArraySlot> arraySlots,
        int dialogueArgIndex = 2)
    {
        if (translated.Length != kinds.Count)
            return;

        for (var i = 0; i < kinds.Count; i++)
        {
            if (kinds[i] == TextKind.Dialogue && !string.IsNullOrEmpty(translated[i]))
                args[dialogueArgIndex] = Il2CppStringHelper.Write(translated[i]);
        }

        var slotCursor = 0;
        for (var i = 0; i < kinds.Count; i++)
        {
            if (kinds[i] != TextKind.Choice)
                continue;

            if (slotCursor >= arraySlots.Count)
                break;

            if (!string.IsNullOrEmpty(translated[i]))
                WriteStringArraySlot(arraySlots[slotCursor], translated[i]);

            slotCursor++;
        }
    }

    private static void WriteStringArraySlot(StringArraySlot slot, string translated)
    {
        if (slot.Array is string[] strings)
        {
            strings[slot.Index] = translated;
            return;
        }

        Il2CppArrayReflection.SetStringAt(slot.Array, slot.Index, translated);
    }

    private static void CollectChoiceTexts(
        object dialogChoices,
        List<string> texts,
        List<TextKind> kinds,
        List<ChoiceTarget> targets)
    {
        if (dialogChoices == null)
            return;

        var listType = dialogChoices.GetType();

        if (dialogChoices is IEnumerable managedEnumerable && listType.Namespace?.StartsWith("System") == true)
        {
            foreach (var item in managedEnumerable)
                TryAddChoice(item, texts, kinds, targets);
            return;
        }

        var countProp = listType.GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
        var getItem = listType.GetMethod("get_Item", BindingFlags.Public | BindingFlags.Instance);
        if (countProp == null || getItem == null)
            return;

        var count = (int)countProp.GetValue(dialogChoices)!;
        for (var i = 0; i < count; i++)
        {
            var item = getItem.Invoke(dialogChoices, new object[] { i });
            TryAddChoice(item, texts, kinds, targets);
        }
    }

    private static void TryAddChoice(
        object item,
        List<string> texts,
        List<TextKind> kinds,
        List<ChoiceTarget> targets)
    {
        if (item == null)
            return;

        var itemType = item.GetType();
        var label = ReadChoiceText(item, itemType);
        if (string.IsNullOrWhiteSpace(label))
            return;

        texts.Add(label);
        kinds.Add(TextKind.Choice);
        targets.Add(new ChoiceTarget { Item = item, ItemType = itemType });
    }

    private static string? ReadChoiceText(object item, Type itemType)
    {
        var prop = itemType.GetProperty("choiceLabel", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop?.CanRead == true)
        {
            var value = Il2CppStringHelper.Read(prop.GetValue(item));
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        var getter = itemType.GetMethod("get_choiceLabel", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (getter != null)
        {
            var value = Il2CppStringHelper.Read(getter.Invoke(item, null));
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        foreach (var field in itemType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (!Il2CppStringHelper.IsStringType(field.FieldType))
                continue;

            if (!field.Name.Contains("choiceLabel", StringComparison.OrdinalIgnoreCase))
                continue;

            var value = Il2CppStringHelper.Read(field.GetValue(item));
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static void WriteChoiceText(object item, Type itemType, string translated)
    {
        if (itemType.GetProperty("choiceLabel", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) is { CanWrite: true } prop)
        {
            prop.SetValue(item, Il2CppStringHelper.Write(translated));
            return;
        }

        var setter = itemType.GetMethod("set_choiceLabel", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (setter != null)
        {
            setter.Invoke(item, new[] { Il2CppStringHelper.Write(translated) });
            return;
        }

        foreach (var field in itemType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (!Il2CppStringHelper.IsStringType(field.FieldType)
                || !field.Name.Contains("choiceLabel", StringComparison.OrdinalIgnoreCase))
                continue;

            field.SetValue(item, Il2CppStringHelper.Write(translated));
            return;
        }

        TalkLog.Warn($"Nao foi possivel gravar choiceLabel em {itemType.Name}");
    }
}

#endregion

#region Interaction
// --- InteractionClickPatch.cs ---
/// <summary>
/// Hooks de clique legados (OnNewTarget, MouseOver, UpdateInteractorName) desligados.
/// Captura essencial via PatchSessionHooks: ProcessSelectEntity, ProcessStartInteraction,
/// ProcessPreTalkScreen, OnPreTalk.
/// </summary>
internal static class InteractionClickPatch
{
    private const string SelectionControllerTypeName = "UISelectionController";

    internal static int PatchAll(Harmony harmony, Type interactionControllerType)
    {
        // Desligado: OnNewTarget, InvokeOnNewTarget, UpdateInteractButton, MouseOver, UpdateInteractorName.
        // Essencial em TalkScreenPatch.PatchSessionHooks.
        LogAscii.LogInfo(
            "  click hooks nao-essenciais OFF (OnNewTarget/MouseOver/UpdateInteractButton/UpdateInteractorName)");

        var selectionController = ResolveType(SelectionControllerTypeName);
        return SelectionDebugProbe.PatchDebugHooks(harmony, selectionController);
    }

    private static Type? ResolveType(string typeName)
    {
        var type = AccessTools.TypeByName(typeName);
        if (type != null)
            return type;

        foreach (var candidate in AccessTools.AllTypes())
        {
            if (candidate.Name == typeName)
                return candidate;
        }

        return null;
    }
}

// --- UiInteractionFinder.cs ---
/// <summary>
/// Localiza UIInteractionController no cenario (fallback quando o hook nao recebe __instance).
/// </summary>
internal static class UiInteractionFinder
{
    private static Type? _controllerType;
    private static MethodInfo? _findObjectOfType;
    private static MethodInfo? _findObjectsOfType;
    private static object? _cachedController;

    internal static bool HasCachedController => _cachedController != null;

    internal static void Init(Type controllerType)
    {
        _controllerType = controllerType;
        _findObjectOfType = null;
        _findObjectsOfType = null;
        _cachedController = null;

        var objectType = AccessTools.TypeByName("UnityEngine.Object")
                         ?? AccessTools.TypeByName("Il2CppUnityEngine.Object");
        if (objectType == null)
            return;

        _findObjectOfType = AccessTools.Method(objectType, "FindObjectOfType", new[] { typeof(Type) })
                            ?? AccessTools.Method(objectType, "FindObjectOfType", new[] { typeof(Type), typeof(bool) });

        foreach (var method in objectType.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.Name is not ("FindObjectsOfType" or "FindObjectsByType"))
                continue;
            if (method.GetParameters().Length is not (1 or 2))
                continue;
            if (method.GetParameters()[0].ParameterType != typeof(Type))
                continue;

            _findObjectsOfType = method;
            break;
        }
    }

    /// <summary>Resolve controller a partir do __instance do hook Harmony.</summary>
    internal static object? Resolve(object? hookInstance)
    {
        if (hookInstance != null)
        {
            Remember(hookInstance);
            if (IsController(hookInstance))
                return hookInstance;

            var nested = FindControllerInObject(hookInstance, depth: 0, maxDepth: 2);
            if (nested != null)
            {
                Remember(nested);
                return nested;
            }
        }

        return GetCachedController();
    }

    /// <summary>Guarda instancia recebida nos hooks Harmony (__instance).</summary>
    internal static void Remember(object? controller)
    {
        if (controller == null || !IsController(controller))
            return;

        if (ReferenceEquals(_cachedController, controller))
            return;

        _cachedController = controller;
        TraceLog.Event($"UiInteractionFinder.remember type={controller.GetType().FullName}");
    }

    internal static void ClearCache()
    {
        _cachedController = null;
    }

    internal static object? GetCachedController() => _cachedController ?? FindControllerInScene();

    internal static object? GetController() => GetCachedController();

    internal static bool IsKnownController(object? obj) => obj != null && IsController(obj);

    private static bool IsController(object obj) =>
        _controllerType != null && _controllerType.IsInstanceOfType(obj);

    private static object? FindControllerInObject(object root, int depth, int maxDepth)
    {
        if (depth > maxDepth)
            return null;

        var type = root.GetType();
        for (var current = type; current != null; current = current.BaseType)
        {
            foreach (var field in current.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (field.FieldType == typeof(string) || field.FieldType.IsPrimitive)
                    continue;

                object? nested;
                try
                {
                    nested = field.GetValue(root);
                }
                catch
                {
                    continue;
                }

                if (nested == null || ReferenceEquals(nested, root))
                    continue;

                if (IsController(nested))
                    return nested;

                if (depth + 1 <= maxDepth && field.FieldType.IsClass)
                {
                    var deeper = FindControllerInObject(nested, depth + 1, maxDepth);
                    if (deeper != null)
                        return deeper;
                }
            }

            foreach (var prop in current.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                    continue;
                if (prop.PropertyType == typeof(string) || prop.PropertyType.IsPrimitive)
                    continue;

                object? nested;
                try
                {
                    nested = prop.GetValue(root);
                }
                catch
                {
                    continue;
                }

                if (nested == null || ReferenceEquals(nested, root))
                    continue;

                if (IsController(nested))
                    return nested;

                if (depth + 1 <= maxDepth && prop.PropertyType.IsClass)
                {
                    var deeper = FindControllerInObject(nested, depth + 1, maxDepth);
                    if (deeper != null)
                        return deeper;
                }
            }
        }

        return null;
    }

    private static object? FindControllerInScene()
    {
        if (_controllerType == null)
            return null;

        var single = TryFindSingle();
        if (single != null)
        {
            Remember(single);
            return single;
        }

        var fromArray = TryFindFromArray();
        if (fromArray != null)
        {
            Remember(fromArray);
            return fromArray;
        }

        return null;
    }

    private static object? TryFindSingle()
    {
        if (_findObjectOfType == null)
            return null;

        try
        {
            var parameters = _findObjectOfType.GetParameters();
            return parameters.Length == 1
                ? _findObjectOfType.Invoke(null, new object[] { _controllerType! })
                : _findObjectOfType.Invoke(null, new object[] { _controllerType!, false });
        }
        catch (Exception ex)
        {
            TraceLog.Error("UiInteractionFinder.TryFindSingle", ex);
            return null;
        }
    }

    private static object? TryFindFromArray()
    {
        if (_findObjectsOfType == null)
            return TryResourcesFindAll();

        try
        {
            var parameters = _findObjectsOfType.GetParameters();
            var args = parameters.Length == 1
                ? new object[] { _controllerType! }
                : new object[] { _controllerType!, false };
            var result = _findObjectsOfType.Invoke(null, args);
            return FirstControllerFromCollection(result);
        }
        catch (Exception ex)
        {
            TraceLog.Error("UiInteractionFinder.TryFindFromArray", ex);
            return TryResourcesFindAll();
        }
    }

    private static object? TryResourcesFindAll()
    {
        var resourcesType = AccessTools.TypeByName("UnityEngine.Resources")
                            ?? AccessTools.TypeByName("Il2CppUnityEngine.Resources");
        if (resourcesType == null || _controllerType == null)
            return null;

        var method = AccessTools.Method(resourcesType, "FindObjectsOfTypeAll", new[] { typeof(Type) });
        if (method == null)
            return null;

        try
        {
            return FirstControllerFromCollection(method.Invoke(null, new object[] { _controllerType }));
        }
        catch (Exception ex)
        {
            TraceLog.Error("UiInteractionFinder.TryResourcesFindAll", ex);
            return null;
        }
    }

    private static object? FirstControllerFromCollection(object? collection)
    {
        if (collection == null || _controllerType == null)
            return null;

        if (collection is Array managedArray)
        {
            foreach (var item in managedArray)
            {
                if (item != null && IsController(item))
                    return item;
            }

            return null;
        }

        try
        {
            Il2CppArrayReflection.EnsureInitialized(collection);
            var length = Il2CppArrayReflection.GetLength(collection);
            for (var i = 0; i < length; i++)
            {
                var item = Il2CppArrayReflection.GetObjectAt(collection, i);
                if (item != null && IsController(item))
                    return item;
            }
        }
        catch (Exception ex)
        {
            TraceLog.Error("UiInteractionFinder.FirstControllerFromCollection", ex);
        }

        return null;
    }
}

#endregion

#region Ui
// --- UiTextPatch.cs ---
/// <summary>
/// Traduz UI estatica (login, rotulos) via CdnStringStore — mesmo papel do antigo Translator.dll (UITools.SetText).
/// </summary>
internal static class UiTextPatch
{
    private const string UiToolsTypeName = "UITools";

    [ThreadStatic]
    private static bool _guard;

    internal static int PatchAll(Harmony harmony)
    {
        if (!CdnStringStore.UseLookup)
        {
            TraceLog.Event("UiTextPatch.skip UseLookup=false");
            LogAscii.LogInfo("  UiTextPatch: UseCdnFallback=false — sem patch de UI estatica.");
            return 0;
        }

        TraceLog.Event("UiTextPatch.begin");
        var count = 0;
        count += PatchUiToolsSetText(harmony);
        count += PatchStringSetter(harmony, "TMPro.TMP_Text", "set_text");
        count += PatchStringSetter(harmony, "UnityEngine.UI.Text", "set_text");
        count += PatchStringSetter(harmony, "UnityEngine.TextMesh", "set_text");
        count += PatchTmpSetTextMethods(harmony);
        TraceLog.Event($"UiTextPatch.end patches={count}");
        LogAscii.LogInfo($"  UiTextPatch: {count} hooks (UITools/TMP/Text — CDN lookup)");
        return count;
    }

    public static void UiToolsSetTextPrefix(object[] __args) => TryReplaceAllArgs(__args);

    public static void UiToolsSetTextPostfix(object[] __args) => TryReplaceAllArgs(__args);

    public static void TextSetterPrefix(object[] __args)
    {
        if (_guard || __args == null || __args.Length < 1)
            return;

        _guard = true;
        try
        {
            TryReplaceAllArgs(__args);
        }
        finally
        {
            _guard = false;
        }
    }

    public static void TextSetterPostfix(object[] __args) => TextSetterPrefix(__args);

    public static void TmpSetTextPrefix(object[] __args) => TryReplaceAllArgs(__args);

    public static void TmpSetTextPostfix(object[] __args) => TryReplaceAllArgs(__args);

    private static void TryReplaceAllArgs(object[]? args)
    {
        if (_guard || args == null)
            return;

        for (var i = 0; i < args.Length; i++)
            TryReplaceArg(args, i);
    }

    private static void TryReplaceArg(object[] args, int index)
    {
        if (_guard || index < 0 || index >= args.Length)
            return;

        var text = Il2CppStringHelper.Read(args[index]);
        if (ShouldSkipLookup(text))
            return;

        if (!CdnStringStore.TryTranslate(text, out var translated, out var sourceFile)
            || string.IsNullOrEmpty(translated))
            return;

        if (string.Equals(text, translated, StringComparison.Ordinal))
            return;

        args[index] = Il2CppStringHelper.Write(translated);
        TraceLog.Counter("UiTextPatch.replaced");
        TraceLog.Event($"UiTextPatch.replaced \"{TalkLog.Preview(text)}\" -> \"{TalkLog.Preview(translated)}\" ({sourceFile})");

        if (TranslateClient.LogVerbose)
            TalkLog.Info($"ui CDN ({sourceFile}): \"{TalkLog.Preview(text)}\" -> \"{TalkLog.Preview(translated)}\"");
    }

    private static bool ShouldSkipLookup(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        return text.Length <= 1
               || text is "(None)" or "-" or "..." or "—";
    }

    private static int PatchUiToolsSetText(Harmony harmony)
    {
        var uiTools = AccessTools.TypeByName(UiToolsTypeName);
        if (uiTools == null)
        {
            LogAscii.LogWarning("UITools nao encontrado — UI estatica sem patch.");
            TraceLog.Event("UiTextPatch.UITools.missing");
            return 0;
        }

        var prefix = new HarmonyMethod(typeof(UiTextPatch), nameof(UiToolsSetTextPrefix));
        var postfix = new HarmonyMethod(typeof(UiTextPatch), nameof(UiToolsSetTextPostfix));
        var count = 0;

        foreach (var method in AccessTools.GetDeclaredMethods(uiTools))
        {
            if (method.Name != "SetText")
                continue;

            var hasString = false;
            foreach (var p in method.GetParameters())
            {
                if (Il2CppStringHelper.IsStringType(p.ParameterType))
                {
                    hasString = true;
                    break;
                }
            }

            if (!hasString)
                continue;

            harmony.Patch(method, prefix: prefix, postfix: postfix);
            TraceLog.Event($"UiTextPatch.UITools {method.DeclaringType?.FullName}.{method.Name}");
            LogAscii.LogInfo($"  ui text {method.Name}({method.GetParameters().Length} args)");
            count++;
        }

        return count;
    }

    private static int PatchTmpSetTextMethods(Harmony harmony)
    {
        var tmpType = AccessTools.TypeByName("TMPro.TMP_Text")
                      ?? AccessTools.TypeByName("TMP_Text");
        if (tmpType == null)
        {
            TraceLog.Event("UiTextPatch.TMP_Text.missing");
            return 0;
        }

        var prefix = new HarmonyMethod(typeof(UiTextPatch), nameof(TmpSetTextPrefix));
        var postfix = new HarmonyMethod(typeof(UiTextPatch), nameof(TmpSetTextPostfix));
        var count = 0;

        foreach (var method in AccessTools.GetDeclaredMethods(tmpType))
        {
            if (method.Name != "SetText")
                continue;

            var hasString = false;
            foreach (var p in method.GetParameters())
            {
                if (Il2CppStringHelper.IsStringType(p.ParameterType))
                {
                    hasString = true;
                    break;
                }
            }

            if (!hasString)
                continue;

            harmony.Patch(method, prefix: prefix, postfix: postfix);
            TraceLog.Event($"UiTextPatch.TMP_SetText {method.DeclaringType?.FullName}.{method.Name}");
            LogAscii.LogInfo($"  ui text TMP_Text.{method.Name}");
            count++;
        }

        return count;
    }

    private static int PatchStringSetter(Harmony harmony, string typeName, string memberName)
    {
        var type = AccessTools.TypeByName(typeName);
        if (type == null)
        {
            TraceLog.Event($"UiTextPatch.missing type={typeName}");
            return 0;
        }

        var setter = AccessTools.PropertySetter(type, "text")
                     ?? AccessTools.Method(type, memberName, new[] { typeof(string) });
        if (setter == null)
            return 0;

        var prefix = new HarmonyMethod(typeof(UiTextPatch), nameof(TextSetterPrefix));
        var postfix = new HarmonyMethod(typeof(UiTextPatch), nameof(TextSetterPostfix));
        harmony.Patch(setter, prefix: prefix, postfix: postfix);
        TraceLog.Event($"UiTextPatch.setter {type.FullName}.{setter.Name}");
        LogAscii.LogInfo($"  ui text {type.Name}.text");
        return 1;
    }
}

#endregion

#region Debug
// --- NpcIdDebugProbe.cs ---
/// <summary>
/// Debug isolado — procura npcId em args/objetos dos hooks de Talk (nao altera coleta).
/// </summary>
internal static class NpcIdDebugProbe
{
    private const int MaxDepth = 3;
    private const int MaxObjects = 48;

    internal static void ScanHook(string hookName, object[]? args, object? extra = null)
    {
        if (!PluginSettings.NpcIdDebug)
            return;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var ints = new List<string>();
        var visited = new HashSet<int>();

        ScanArgs(args, ids, ints, visited);
        if (extra != null)
            ScanObject(extra, ids, ints, visited, depth: 0, label: extra.GetType().Name);

        if (ids.Count == 0 && ints.Count == 0)
            return;

        var summary = new StringBuilder();
        summary.Append($"hook={hookName}");
        summary.Append($" | entity={TalkSession.SelectedEntityId}");
        if (!string.IsNullOrWhiteSpace(TalkSession.NpcName))
            summary.Append($" | name=\"{TalkLog.Preview(TalkSession.NpcName)}\"");
        if (ints.Count > 0)
            summary.Append(" | ints: ").Append(string.Join(", ", ints));
        if (ids.Count > 0)
            summary.Append(" | ids: ").Append(string.Join(", ", ids));

        TalkLog.DebugNpcId(summary.ToString());
        TraceLog.Event($"NpcIdDebugProbe {summary}");
    }

    internal static void RememberFromIds(IEnumerable<string> ids)
    {
        foreach (var id in ids)
        {
            if (!NpcIdPatterns.LooksLikeNpcId(id))
                continue;

            EntityNpcIdCache.Remember(
                TalkSession.SelectedEntityId,
                TalkSession.NpcName,
                id);
            return;
        }
    }

    private static void ScanArgs(object[]? args, ISet<string> ids, List<string> ints, HashSet<int> visited)
    {
        if (args == null)
            return;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == null)
                continue;

            if (arg is string || Il2CppStringHelper.IsStringType(arg.GetType()))
            {
                ConsiderString(Il2CppStringHelper.Read(arg), ids, ints, $"arg{i}");
                continue;
            }

            if (arg is int intValue)
            {
                if (intValue != 0)
                    ints.Add($"arg{i}={intValue}");
                continue;
            }

            ScanObject(arg, ids, ints, visited, depth: 0, label: $"arg{i}:{arg.GetType().Name}");
        }
    }

    private static void ScanObject(
        object obj,
        ISet<string> ids,
        List<string> ints,
        HashSet<int> visited,
        int depth,
        string label)
    {
        if (depth > MaxDepth || visited.Count >= MaxObjects)
            return;

        var key = obj.GetHashCode();
        if (!visited.Add(key))
            return;

        var type = obj.GetType();
        if (Il2CppStringHelper.IsStringType(type))
        {
            ConsiderString(Il2CppStringHelper.Read(obj), ids, ints, label);
            return;
        }

        if (type == typeof(int))
        {
            var value = (int)obj;
            if (value != 0)
                ints.Add($"{label}={value}");
            return;
        }

        for (var current = type; current != null && depth < MaxDepth; current = current.BaseType)
        {
            foreach (var prop in current.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                    continue;

                ReadMember(
                    () => prop.GetValue(obj),
                    prop.PropertyType,
                    prop.Name,
                    ids,
                    ints,
                    visited,
                    depth,
                    obj);
            }

            foreach (var field in current.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                ReadMember(
                    () => field.GetValue(obj),
                    field.FieldType,
                    field.Name,
                    ids,
                    ints,
                    visited,
                    depth,
                    obj);
            }
        }
    }

    private static void ReadMember(
        Func<object?> read,
        Type memberType,
        string memberName,
        ISet<string> ids,
        List<string> ints,
        HashSet<int> visited,
        int depth,
        object owner)
    {
        try
        {
            if (memberType == typeof(int))
            {
                var value = (int)read()!;
                if (value != 0)
                    ints.Add($"{memberName}={value}");
                return;
            }

            if (Il2CppStringHelper.IsStringType(memberType))
            {
                ConsiderString(Il2CppStringHelper.Read(read()), ids, ints, memberName);
                return;
            }

            if (!memberType.IsClass || memberType == typeof(string))
                return;

            var nested = read();
            if (nested == null || nested is string || ReferenceEquals(nested, owner))
                return;

            if (nested is IEnumerable && nested is not IList)
                return;

            ScanObject(nested, ids, ints, visited, depth + 1, memberName);
        }
        catch
        {
            // Il2CPP reflection can fail on stripped members.
        }
    }

    private static void ConsiderString(string? value, ISet<string> ids, List<string> ints, string source)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        value = value.Trim();
        if (NpcIdPatterns.IsCandidateToken(value))
            ids.Add(value);
        else if (value.Length <= 32 && int.TryParse(value, out var parsed) && parsed > 0)
            ints.Add($"{source}={parsed}");
    }
}

// --- SelectionDebugProbe.cs ---
/// <summary>
/// Debug isolado — inspeciona UISelection no clique (nao altera TalkSession / coleta).
/// </summary>
internal static class SelectionDebugProbe
{
    private static readonly string[] TargetMethodNames = { "OnNewTarget", "InvokeOnNewTarget" };

    internal static int PatchDebugHooks(HarmonyLib.Harmony harmony, Type? selectionController)
    {
        if (!PluginSettings.SelectionDebug || selectionController == null)
            return 0;

        var postfix = new HarmonyLib.HarmonyMethod(typeof(SelectionDebugProbe), nameof(Postfix));
        var count = 0;

        foreach (var methodName in TargetMethodNames)
        {
            foreach (var method in selectionController.GetMethods(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.IsAbstract || method.IsStatic || method.Name != methodName)
                    continue;
                if (method.GetParameters().Length != 1)
                    continue;

                try
                {
                    if (!HarmonyPatchGuard.PatchPostfix(harmony, method, postfix))
                        continue;

                    TraceLog.Event($"SelectionDebugProbe.patch {selectionController.Name}.{method.Name}");
                    LogAscii.LogInfo($"  debug-selecao {selectionController.Name}.{method.Name}");
                    count++;
                }
                catch (Exception ex)
                {
                    TraceLog.Event($"SelectionDebugProbe.skip {method.Name} {ex.GetType().Name}");
                }
            }
        }

        return count;
    }

    public static void Postfix(object __0)
    {
        if (!PluginSettings.SelectionDebug || __0 == null)
            return;

        LogFields(__0);
    }

    internal static void LogFields(object selection)
    {
        var type = selection.GetType();
        var strings = new List<string>();
        var ints = new List<string>();

        for (var current = type; current != null; current = current.BaseType)
        {
            foreach (var prop in current.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                    continue;

                TryReadMember(selection, prop.Name, prop.PropertyType, () => prop.GetValue(selection), strings, ints);
            }

            foreach (var field in current.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                TryReadMember(selection, field.Name, field.FieldType, () => field.GetValue(selection), strings, ints);
        }

        strings.Sort(StringComparer.OrdinalIgnoreCase);
        ints.Sort(StringComparer.OrdinalIgnoreCase);

        var summary = new StringBuilder();
        summary.Append($"tipo={type.Name}");
        if (ints.Count > 0)
            summary.Append(" | ints: ").Append(string.Join(", ", ints));
        if (strings.Count > 0)
            summary.Append(" | strings: ").Append(string.Join(", ", strings));

        TalkLog.DebugSelecao(summary.ToString());
        TraceLog.Event($"SelectionDebugProbe.fields {summary}");
    }

    private static void TryReadMember(
        object selection,
        string memberName,
        Type memberType,
        Func<object?> read,
        List<string> strings,
        List<string> ints)
    {
        try
        {
            if (memberType == typeof(int))
            {
                var value = (int)read()!;
                if (value != 0)
                    ints.Add($"{memberName}={value}");
                return;
            }

            if (Il2CppStringHelper.IsStringType(memberType))
            {
                var text = Il2CppStringHelper.Read(read());
                if (string.IsNullOrWhiteSpace(text) || text.Length > 160)
                    return;
                strings.Add($"{memberName}=\"{TalkLog.Preview(text)}\"");
                return;
            }

            if (memberType.IsClass && memberType != typeof(string))
            {
                var nested = read();
                if (nested == null || nested is string || nested == selection)
                    return;

                var nestedType = nested.GetType();
                if (nestedType.Name.Contains("UISelection", StringComparison.Ordinal)
                    || nestedType.Name.Contains("Npc", StringComparison.OrdinalIgnoreCase)
                    || nestedType.Name.Contains("Entity", StringComparison.OrdinalIgnoreCase))
                {
                    strings.Add($"{memberName}=<{nestedType.Name}>");
                }
            }
        }
        catch
        {
            // Il2CPP reflection can fail on stripped members.
        }
    }
}

// --- TalkDiagnostics.cs ---
/// <summary>
/// Contadores de hooks — grava em hook-stats.log sem depender do nível de log do BepInEx.
/// </summary>
internal static class TalkDiagnostics
{
    private static long _showTalk;
    private static long _processTalkScreen;
    private static long _processPreTalkScreen;
    private static long _processStartInteraction;
    private static long _processSelectEntity;
    private static long _processEndInteraction;
    private static long _onPreTalk;
    private static long _updateFancyMenu;
    private static long _updateFancyMenuOptions;
    private static long _uiSelection;
    private static long _updateInteractorName;
    private static long _sessionOpen;
    private static long _sessionClose;
    private static long _dynamicApply;
    private static long _dynamicRemove;

    private static long _lastShowTalk;
    private static long _lastProcessTalkScreen;
    private static long _lastProcessPreTalkScreen;
    private static long _lastProcessStartInteraction;
    private static long _lastProcessSelectEntity;
    private static long _lastProcessEndInteraction;
    private static long _lastOnPreTalk;
    private static long _lastUpdateFancyMenu;
    private static long _lastUpdateFancyMenuOptions;
    private static long _lastUiSelection;
    private static long _lastUpdateInteractorName;

    private static CancellationTokenSource? _cts;
    private static string _logPath = "";

    internal static void Start(int intervalSec)
    {
        _logPath = Path.Combine(Paths.BepInExRootPath, "plugins", "PgTranslateLive", "hook-stats.log");
        _cts = new CancellationTokenSource();
        WriteHeader();

        Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, intervalSec)), _cts.Token)
                        .ConfigureAwait(false);
                    WriteSnapshot();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogAscii.LogWarning($"TalkDiagnostics: {ex.Message}");
                }
            }
        });
    }

    internal static void Stop() => _cts?.Cancel();

    internal static void HitShowTalk() => Interlocked.Increment(ref _showTalk);
    internal static void HitProcessTalkScreen() => Interlocked.Increment(ref _processTalkScreen);
    internal static void HitProcessPreTalkScreen() => Interlocked.Increment(ref _processPreTalkScreen);
    internal static void HitProcessStartInteraction() => Interlocked.Increment(ref _processStartInteraction);
    internal static void HitProcessSelectEntity() => Interlocked.Increment(ref _processSelectEntity);
    internal static void HitProcessEndInteraction() => Interlocked.Increment(ref _processEndInteraction);
    internal static void HitOnPreTalk() => Interlocked.Increment(ref _onPreTalk);
    internal static void HitUpdateFancyMenu() => Interlocked.Increment(ref _updateFancyMenu);
    internal static void HitUpdateFancyMenuOptions() => Interlocked.Increment(ref _updateFancyMenuOptions);
    internal static void HitUiSelection() => Interlocked.Increment(ref _uiSelection);
    internal static void HitUpdateInteractorName() => Interlocked.Increment(ref _updateInteractorName);
    internal static void HitSessionOpen() => Interlocked.Increment(ref _sessionOpen);
    internal static void HitSessionClose() => Interlocked.Increment(ref _sessionClose);
    internal static void HitDynamicApply() => Interlocked.Increment(ref _dynamicApply);
    internal static void HitDynamicRemove() => Interlocked.Increment(ref _dynamicRemove);

    private static void WriteHeader()
    {
        var line =
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} DIAG — hook-stats.log (intervalo {PluginSettings.DiagnosticIntervalSec}s)\r\n" +
            "Colunas: total | delta_intervalo\r\n";
        File.AppendAllText(_logPath, line);
    }

    private static void WriteSnapshot()
    {
        var st = Interlocked.Read(ref _showTalk);
        var pts = Interlocked.Read(ref _processTalkScreen);
        var ppt = Interlocked.Read(ref _processPreTalkScreen);
        var psi = Interlocked.Read(ref _processStartInteraction);
        var pse = Interlocked.Read(ref _processSelectEntity);
        var pei = Interlocked.Read(ref _processEndInteraction);
        var opt = Interlocked.Read(ref _onPreTalk);
        var ufm = Interlocked.Read(ref _updateFancyMenu);
        var ufmo = Interlocked.Read(ref _updateFancyMenuOptions);
        var uis = Interlocked.Read(ref _uiSelection);
        var uin = Interlocked.Read(ref _updateInteractorName);
        var so = Interlocked.Read(ref _sessionOpen);
        var sc = Interlocked.Read(ref _sessionClose);
        var da = Interlocked.Read(ref _dynamicApply);
        var dr = Interlocked.Read(ref _dynamicRemove);

        var dst = st - _lastShowTalk;
        var dpts = pts - _lastProcessTalkScreen;
        var dppt = ppt - _lastProcessPreTalkScreen;
        var dpsi = psi - _lastProcessStartInteraction;
        var dpse = pse - _lastProcessSelectEntity;
        var dpei = pei - _lastProcessEndInteraction;
        var dopt = opt - _lastOnPreTalk;
        var dufm = ufm - _lastUpdateFancyMenu;
        var dufmo = ufmo - _lastUpdateFancyMenuOptions;
        var duis = uis - _lastUiSelection;
        var duin = uin - _lastUpdateInteractorName;

        _lastShowTalk = st;
        _lastProcessTalkScreen = pts;
        _lastProcessPreTalkScreen = ppt;
        _lastProcessStartInteraction = psi;
        _lastProcessSelectEntity = pse;
        _lastProcessEndInteraction = pei;
        _lastOnPreTalk = opt;
        _lastUpdateFancyMenu = ufm;
        _lastUpdateFancyMenuOptions = ufmo;
        _lastUiSelection = uis;
        _lastUpdateInteractorName = uin;

        var line =
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} " +
            $"UiSelection={uis}(+{duis}) " +
            $"UpdateInteractorName={uin}(+{duin}) " +
            $"UpdateFancyMenu={ufm}(+{dufm}) " +
            $"UpdateFancyMenuOptions={ufmo}(+{dufmo}) " +
            $"OnPreTalk={opt}(+{dopt}) " +
            $"ProcessSelectEntity={pse}(+{dpse}) " +
            $"ProcessStartInteraction={psi}(+{dpsi}) " +
            $"ProcessPreTalkScreen={ppt}(+{dppt}) " +
            $"ShowTalk={st}(+{dst}) " +
            $"ProcessTalkScreen={pts}(+{dpts}) " +
            $"ProcessEndInteraction={pei}(+{dpei}) " +
            $"SessionOpen={so} SessionClose={sc} DynamicApply={da} DynamicRemove={dr}\r\n";

        File.AppendAllText(_logPath, line);
    }
}

#endregion

#region Plugin
// --- Plugin.cs ---
[BepInPlugin("com.pg.translatelive", "Pg Translate Live", "0.14.45")]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log = null!;

    public override void Load()
    {
        Log = base.Log;

        PluginSettings.Init(Config);

        if (PluginSettings.UltraShellMode)
        {
            LogAscii.LogWarning("ULTRA-SHELL: Load vazio — zero TranslateClient/NpcTalk/Harmony");
            LogAscii.LogInfo($"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} Pg Translate Live v0.14.45 - ULTRA-SHELL");
            return;
        }

        using var loadScope = TraceLog.Scope("Plugin.Load");

        TraceLog.Start();
        TraceLog.Event("PluginSettings.Init.complete");

        if (PluginSettings.TalkEnabled)
        {
            using (TraceLog.Scope("TranslateClient.Init"))
                TranslateClient.Init(Config);

            using (TraceLog.Scope("NpcTalkStore.Init"))
                NpcTalkStore.Init(Config);

            using (TraceLog.Scope("NpcRegistryStore.Init"))
                NpcRegistryStore.Init(Config);
        }

        if (PluginSettings.TalkEnabled || PluginSettings.LabelsEnabled)
        {
            using (TraceLog.Scope("CdnStringStore.Init"))
                CdnStringStore.Init(Config);
        }

        var talkPatches = 0;
        var uiTextPatches = 0;
        if (PluginSettings.ShellMode)
        {
            TraceLog.Event("Plugin.Load.shellMode=true — Harmony/PatchAll skipped");
            LogAscii.LogWarning("SHELL MODE: sem Harmony/PatchAll — traducao desligada (teste de travada)");
        }
        else
        {
            var harmony = new Harmony("com.pg.translatelive");
            if (PluginSettings.TalkEnabled)
            {
                using (TraceLog.Scope("TalkScreenPatch.PatchAll"))
                    talkPatches = TalkScreenPatch.PatchAll(harmony);
            }

            if (PluginSettings.LabelsEnabled)
                uiTextPatches = UiTextPatch.PatchAll(harmony);
        }

        var mode = PluginSettings.ShellMode
            ? "SHELL"
            : PluginSettings.LabelsOnly
                ? "LabelsOnly"
                : PluginSettings.DiscoveryMode && PluginSettings.DiagnosticMode
                    ? "DISCOVERY+DIAGNOSTIC"
                    : PluginSettings.DiscoveryMode
                        ? "DISCOVERY"
                        : PluginSettings.DiagnosticMode
                            ? "DIAGNOSTIC"
                            : PluginSettings.Strategy.ToString();

        TraceLog.Event($"Plugin.Load.summary mode={mode} talk={PluginSettings.TalkEnabled} labels={PluginSettings.LabelsEnabled} talkPatches={talkPatches} uiTextPatches={uiTextPatches}");
        LogAscii.LogInfo($"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} Pg Translate Live v0.14.45 - {mode}");
        LogAscii.LogInfo($"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} Patches: talk={talkPatches} uiText={uiTextPatches}");
        if (PluginSettings.TalkEnabled)
            LogAscii.LogInfo($"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} UseGoogle: {TranslateClient.UseGoogle} | TalkVerbose: {TranslateClient.LogVerbose}");
        LogAscii.LogInfo($"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} TranslationDir: {GameDataPaths.TranslationDir}");
        if (PluginSettings.LabelsOnly)
            LogAscii.LogInfo("  LabelsOnly: Falar desligado — so rotulos CDN (sem IndexMethodsGlobally)");
    }
}

#endregion

