using System.Globalization;
using System.Text;

namespace PgTranslateLive;

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
