using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BepInEx.Configuration;

namespace PgTranslateLive;

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
    private static ConfigEntry<int> _googleWorkers = null!;

    internal static bool Enabled => _enabled?.Value ?? false;
    internal static bool LogVerbose => _talkVerbose?.Value ?? true;
    internal static bool UseGoogle => _useGoogle?.Value ?? true;

    internal static void Init(ConfigFile config)
    {
        _enabled = config.Bind("General", "Enabled", true,
            "Traduz dialogos de NPC (Falar) via Google.");
        _timeoutMs = config.Bind("General", "TimeoutMs", 30000,
            "Timeout HTTP em ms (Google pode levar 10-30 s em batch).");
        _minLength = config.Bind("General", "MinLength", 3,
            "Ignora strings curtas (UI generica).");
        _minDialogueLength = config.Bind("Dialogue", "MinLength", 15,
            "Minimo de caracteres para traduzir falas de NPC.");
        _minChoiceLength = config.Bind("Dialogue", "MinChoiceLength", 3,
            "Minimo de caracteres para traduzir botoes de escolha.");
        _talkVerbose = config.Bind("General", "TalkVerbose", true,
            "Logs detalhados do fluxo Falar.");
        _useGoogle = config.Bind("General", "UseGoogle", true,
            "Se true, consulta Google Translate.");
        _googleWorkers = config.Bind("General", "GoogleWorkers", 4,
            "Threads paralelas para Google no batch.");

        SharedHttp.Timeout = TimeSpan.FromMilliseconds(Math.Max(1000, _timeoutMs.Value));
    }

    internal static string[] ApplyFromTurn(
        string[] texts,
        TextKind[] kinds,
        LivePhase phase)
    {
        if (!Enabled || texts == null || kinds == null || texts.Length == 0 || !TalkTurn.HasTurn)
            return Array.Empty<string>();

        var speeches = CollectByKind(texts, kinds, TextKind.Dialogue);
        var choices = CollectByKind(texts, kinds, TextKind.Choice);
        var general = CollectByKind(texts, kinds, TextKind.General);
        TalkLog.PhaseApplyOnly(phase, speeches.Count > 0 ? speeches : general, choices);

        return TalkTurn.Apply(texts, kinds);
    }

    internal static string[] TryTranslateBatch(
        string[] texts,
        TextKind[] kinds,
        LivePhase? phase = null)
    {
        if (!Enabled || texts == null || kinds == null || texts.Length == 0)
            return Array.Empty<string>();

        if (texts.Length != kinds.Length)
            throw new ArgumentException("texts and kinds length mismatch");

        var speeches = CollectByKind(texts, kinds, TextKind.Dialogue);
        var choices = CollectByKind(texts, kinds, TextKind.Choice);
        var general = CollectByKind(texts, kinds, TextKind.General);

        if (phase == LivePhase.ShowTalk)
            TalkTurn.Begin();

        if (phase.HasValue)
        {
            var primary = speeches.Count > 0 ? speeches : general;
            TalkLog.PhaseStart(phase.Value, primary, choices);
        }

        var results = new string[texts.Length];
        var pendingIndices = new List<int>();
        var pendingTexts = new List<string>();
        var pendingKinds = new List<TextKind>();
        var speechPending = new List<string>();
        var choicePending = new List<string>();
        var yamlHits = 0;

        for (var i = 0; i < texts.Length; i++)
        {
            var text = texts[i];
            var kind = kinds[i];
            if (string.IsNullOrWhiteSpace(text) || !PassesMinLength(text, kind))
                continue;

            if (kind == TextKind.Choice && LooksAlreadyTranslated(text))
            {
                TalkLog.Unchanged(kind, text, "botao ja parece traduzido");
                continue;
            }

            if (kind == TextKind.General && LooksAlreadyTranslated(text))
            {
                TalkLog.Unchanged(kind, text, "ja parece PT");
                continue;
            }

            if (NpcYamlStore.TryGet(text, out var cached))
            {
                TalkTurn.Store(text, cached);
                results[i] = cached;
                yamlHits++;
                TalkLog.Unchanged(kind, text, "npcs.yaml");
                continue;
            }

            pendingIndices.Add(i);
            pendingTexts.Add(text);
            pendingKinds.Add(kind);

            if (kind == TextKind.Dialogue)
                speechPending.Add(text);
            else if (kind == TextKind.Choice)
                choicePending.Add(text);
        }

        if (yamlHits > 0)
            TalkLog.Info($"NpcYaml: {yamlHits} texto(s) do cache local (sem Google)");

        if (pendingIndices.Count == 0)
            return results;

        if (!UseGoogle)
        {
            for (var j = 0; j < pendingIndices.Count; j++)
                TalkLog.Unchanged(pendingKinds[j], pendingTexts[j], "UseGoogle=false");
            return results;
        }

        TalkLog.NeedsGoogle(speechPending, choicePending);
        TalkLog.SendingToGoogle(speechPending, choicePending);
        ApplyGoogleBatch(pendingIndices, pendingTexts, pendingKinds, results);

        return results;
    }

    internal static bool PassesMinLength(string text, TextKind kind) =>
        PassesMinLengthInternal(text, kind);

    private static void ApplyGoogleBatch(
        List<int> pendingIndices,
        List<string> pendingTexts,
        List<TextKind> pendingKinds,
        string[] results)
    {
        var workers = Math.Max(1, Math.Min(_googleWorkers.Value, pendingTexts.Count));
        var parallelResults = new string?[pendingTexts.Count];

        try
        {
            Parallel.For(
                0,
                pendingTexts.Count,
                new ParallelOptions { MaxDegreeOfParallelism = workers },
                i => parallelResults[i] = GoogleTranslate.Translate(pendingTexts[i]));

            for (var i = 0; i < pendingTexts.Count; i++)
            {
                var sourceIndex = pendingIndices[i];
                var source = pendingTexts[i];
                var kind = pendingKinds[i];
                var translated = parallelResults[i];

                if (string.IsNullOrEmpty(translated) || translated == source)
                {
                    TalkLog.Unchanged(kind, source, "Google retornou vazio ou identico");
                    continue;
                }

                TalkTurn.Store(source, translated);
                results[sourceIndex] = translated;
                NpcYamlStore.Record(source, translated);
                TalkLog.GoogleTranslated(kind, source, translated);
            }
        }
        catch (Exception ex)
        {
            TalkLog.Warn($"Falha Google batch: {ex.Message} (pode retentar no proximo Falar)");
        }
    }

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
