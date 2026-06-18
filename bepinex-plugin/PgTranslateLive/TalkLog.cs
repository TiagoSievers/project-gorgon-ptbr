using System;
using System.Collections.Generic;
using System.Linq;

namespace PgTranslateLive;

internal enum LivePhase
{
    ShowTalk,
    DisplayTalkScreen,
    UpdateFancyMenuOptions,
}

internal static class TalkLog
{
    internal static bool Enabled => TranslateClient.LogVerbose;

    internal static void Info(string message)
    {
        if (!Enabled)
            return;
        LogAscii.LogInfo($"{Now()} {message}");
    }

    internal static void Warn(string message)
    {
        if (!Enabled)
            return;
        LogAscii.LogWarning($"{Now()} {message}");
    }

    internal static void PhaseApplyOnly(LivePhase phase, IReadOnlyList<string> speeches, IReadOnlyList<string> choices)
    {
        if (!Enabled)
            return;

        switch (phase)
        {
            case LivePhase.DisplayTalkScreen:
                Info("fluxo: Falar > Coletar > Google > Aplicar na tela (sem HTTP)");
                Info("Aplicando traducoes do ShowTalk na tela (sem HTTP).");
                break;
            case LivePhase.UpdateFancyMenuOptions:
                Info("fluxo: Falar > Aplicar botoes/menu (sem HTTP)");
                if (choices.Count > 0)
                    Info("Aplicando botoes ja traduzidos no menu (sem HTTP).");
                break;
        }
    }

    internal static void PhaseStart(LivePhase phase, IReadOnlyList<string> speeches, IReadOnlyList<string> choices)
    {
        if (!Enabled)
            return;

        var speechList = FormatList(speeches);
        var choiceList = FormatList(choices);

        switch (phase)
        {
            case LivePhase.ShowTalk:
                Info("fluxo: Falar > Coletar (ShowTalk) > Google Translate");
                Info("Falar (ShowTalk) -> Google Translate.");
                if (speeches.Count > 0)
                    Info($"Coleta da fala: {speechList}");
                if (choices.Count > 0)
                    Info($"Coleta dos botoes: {choiceList}");
                break;

            case LivePhase.DisplayTalkScreen:
                Info("fluxo: Falar > Coletar > Google > Aplicar na tela (sem HTTP)");
                Info("Montando tela de dialogo traduzida (DisplayTalkScreen).");
                if (speeches.Count > 0)
                    Info($"Fala recebida: {speechList}");
                if (choices.Count > 0)
                    Info($"Botoes recebidos: {choiceList}");
                break;

            case LivePhase.UpdateFancyMenuOptions:
                if (choices.Count > 0)
                    Info($"Atualizando menu de opcoes - botoes: {choiceList}");
                break;
        }
    }

    internal static void NeedsGoogle(
        IReadOnlyList<string> speechPending,
        IReadOnlyList<string> choicePending)
    {
        if (!Enabled)
            return;

        if (speechPending.Count > 0)
            Info($"Google para fala: {FormatList(speechPending)}");

        if (choicePending.Count > 0)
            Info($"Google para botoes: {FormatList(choicePending)}");
    }

    internal static void SendingToGoogle(
        IReadOnlyList<string> speechForApi,
        IReadOnlyList<string> choiceForApi)
    {
        if (!Enabled)
            return;

        if (speechForApi.Count == 0 && choiceForApi.Count == 0)
            return;

        var parts = new List<string>();
        if (speechForApi.Count > 0)
            parts.Add($"fala [{FormatList(speechForApi)}]");
        if (choiceForApi.Count > 0)
            parts.Add($"botoes [{FormatList(choiceForApi)}]");

        Info($"Consultando Google para: {string.Join(" | ", parts)}");
    }

    internal static void GoogleTranslated(TextKind kind, string source, string translated)
    {
        if (!Enabled)
            return;

        if (kind == TextKind.Choice)
            Info($"Botao traduzido (Google): \"{Preview(source)}\" -> \"{Preview(translated)}\"");
        else
            Info($"Fala traduzida (Google): \"{Preview(source)}\" -> \"{Preview(translated)}\"");
    }

    internal static void Unchanged(TextKind kind, string text, string reason)
    {
        if (!Enabled)
            return;

        if (kind == TextKind.Choice)
            Info($"Botao sem alteracao ({reason}): \"{Preview(text)}\"");
        else
            Info($"Fala sem alteracao ({reason}): \"{Preview(text)}\"");
    }

    internal static void ShowingTranslated(string translatedSpeech)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(translatedSpeech))
            return;

        Info($"Exibindo dialogo traduzido na tela: \"{Preview(translatedSpeech, dialogue: true)}\"");
    }

    internal static string Preview(string s, bool dialogue = false) =>
        string.IsNullOrEmpty(s)
            ? "(vazio)"
            : dialogue && s.Length > 120
                ? s[..117] + "..."
                : s.Length <= 80
                    ? s
                    : s[..77] + "...";

    private static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    private static string FormatList(IEnumerable<string> items) =>
        string.Join(", ", items.Select(s => $"\"{Preview(s)}\""));
}
