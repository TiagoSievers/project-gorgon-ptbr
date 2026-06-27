using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;

namespace PgTranslateLive;

/// <summary>
/// Carrega traduções de npcs.yaml (Translator mod) e grava falas novas após Google.
/// </summary>
internal static class NpcYamlStore
{
    private static readonly object Lock = new();
    private static Dictionary<string, string> _entries = new(StringComparer.Ordinal);
    private static string _path = "";

    private static ConfigEntry<bool> _useLookup = null!;
    private static ConfigEntry<bool> _saveNew = null!;

    internal static bool UseLookup => _useLookup?.Value ?? true;
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
        _useLookup = config.Bind("NpcYaml", "UseLookup", true,
            "Consulta npcs.yaml antes do Google Translate.");
        _saveNew = config.Bind("NpcYaml", "SaveNewTranslations", true,
            "Salva falas novas no npcs.yaml após traduzir via Google.");
        Reload();
    }

    internal static void Reload()
    {
        _path = ResolvePath();
        lock (Lock)
            _entries = LoadFile(_path);

        LogAscii.LogInfo(
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} NpcYaml: {_entries.Count} entradas | {_path}");
    }

    internal static bool TryGet(string source, out string translated)
    {
        translated = "";
        if (!UseLookup || string.IsNullOrWhiteSpace(source))
            return false;

        lock (Lock)
            return _entries.TryGetValue(source, out translated!);
    }

    internal static void Record(string source, string translated)
    {
        if (!_saveNew.Value || string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(translated))
            return;

        if (string.Equals(source, translated, StringComparison.Ordinal))
            return;

        lock (Lock)
        {
            if (_entries.ContainsKey(source))
                return;

            _entries[source] = translated;
            WriteFile(_path, _entries);
        }

        TalkLog.Info($"NpcYaml: nova fala salva ({Count} entradas)");
    }

    private static string ResolvePath()
    {
        var auto = Path.Combine(
            Paths.BepInExRootPath,
            "plugins",
            "Translator",
            "translations",
            "pt-BR",
            "npcs.yaml");

        return File.Exists(auto) ? auto : auto;
    }

    private static Dictionary<string, string> LoadFile(string path)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
            return entries;

        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            var pair = SplitKeyValue(trimmed);
            if (pair == null)
                continue;

            var (key, value) = pair.Value;
            if (!string.IsNullOrEmpty(key))
                entries[key] = value;
        }

        return entries;
    }

    private static void WriteFile(string path, Dictionary<string, string> entries)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var header = ReadHeader(path);
        if (header.Count == 0)
        {
            header.Add("# Project Gorgon — npcs (pt-BR)");
            header.Add("# Falas de NPC capturadas ao vivo pelo PgTranslateLive");
        }

        var sb = new StringBuilder();
        foreach (var line in header)
            sb.AppendLine(line);
        sb.AppendLine();

        foreach (var kv in entries.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"{YamlQuote(kv.Key)}: {YamlQuote(kv.Value)}");

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static List<string> ReadHeader(string path)
    {
        var header = new List<string>();
        if (!File.Exists(path))
            return header;

        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#'))
                header.Add(trimmed);
            else if (string.IsNullOrEmpty(trimmed))
                continue;
            else
                break;
        }

        return header;
    }

    private static (string Key, string Value)? SplitKeyValue(string line)
    {
        var inQuotes = false;
        var escape = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (escape)
            {
                escape = false;
                continue;
            }

            if (ch == '\\')
            {
                escape = true;
                continue;
            }

            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (ch == ':' && !inQuotes && i + 1 < line.Length && line[i + 1] == ' ')
            {
                var key = YamlUnquote(line[..i].Trim());
                var value = YamlUnquote(line[(i + 2)..].Trim());
                return (key, value);
            }
        }

        return null;
    }

    private static string YamlQuote(string text)
    {
        var escaped = text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }

    private static string YamlUnquote(string raw)
    {
        raw = raw.Trim();
        if (raw.Length < 2 || raw[0] != '"' || raw[^1] != '"')
            return raw;

        var sb = new StringBuilder();
        for (var i = 1; i < raw.Length - 1; i++)
        {
            var ch = raw[i];
            if (ch == '\\' && i + 1 < raw.Length - 1)
            {
                var next = raw[i + 1];
                if (next == 'n')
                {
                    sb.Append('\n');
                    i++;
                    continue;
                }

                if (next is '"' or '\\')
                {
                    sb.Append(next);
                    i++;
                    continue;
                }
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }
}
