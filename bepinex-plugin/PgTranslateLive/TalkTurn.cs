using System;
using System.Collections.Generic;

namespace PgTranslateLive;

/// <summary>
/// Traduções do Falar atual (ShowTalk → apply nos outros patches, sem HTTP repetido).
/// </summary>
internal static class TalkTurn
{
    [ThreadStatic]
    private static Dictionary<string, string>? _map;

    internal static void Begin()
    {
        _map = new Dictionary<string, string>();
    }

    internal static void Store(string source, string translated)
    {
        if (_map == null || string.IsNullOrEmpty(source) || string.IsNullOrEmpty(translated))
            return;

        _map[source] = translated;
    }

    internal static bool TryGet(string source, out string translated)
    {
        if (_map != null && _map.TryGetValue(source, out var hit) && !string.IsNullOrEmpty(hit))
        {
            translated = hit;
            return true;
        }

        translated = null!;
        return false;
    }

    internal static bool HasTurn => _map is { Count: > 0 };

    internal static string[] Apply(string[] sources, TextKind[] kinds)
    {
        var results = new string[sources.Length];
        for (var i = 0; i < sources.Length; i++)
        {
            if (TryGet(sources[i], out var hit) && hit != sources[i])
                results[i] = hit;
        }

        return results;
    }

    internal static bool AllKnown(string[] sources)
    {
        if (_map == null || sources.Length == 0)
            return false;

        foreach (var s in sources)
        {
            if (string.IsNullOrWhiteSpace(s))
                continue;
            if (!_map.ContainsKey(s))
                return false;
        }

        return true;
    }
}
