using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PgTranslateLive;

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
