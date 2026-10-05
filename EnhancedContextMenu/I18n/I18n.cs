using System.IO;
using System.Text.Json;

namespace EnhancedContextMenu;

/// <summary>UI strings from Data/I18n. Follows Dalamud unless the setting picks a language.</summary>
internal static class I18n
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static Dictionary<string, string> _strings = new(StringComparer.Ordinal);

    public static void Apply()
    {
        var lang = Plugin.C.Language;
        if (lang is not ("en" or "ja"))
            lang = PluginServices.PluginInterface.UiLanguage;
        Load(lang);
    }

    private static void Load(string? langCode)
    {
        var lang = Normalize(langCode);
        var map = Read(lang);
        if (map.Count == 0 && lang != "en")
            map = Read("en");

        _strings = map;
    }

    public static string Get(string key)
    {
        if (_strings.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
            return value;

        return key;
    }

    private static string Normalize(string? langCode)
    {
        var lang = string.IsNullOrWhiteSpace(langCode) ? "en" : langCode.Trim().ToLowerInvariant();
        return lang.Length > 2 ? lang[..2] : lang;
    }

    private static Dictionary<string, string> Read(string lang)
    {
        var dir = PluginServices.PluginInterface.AssemblyLocation.DirectoryName ?? AppContext.BaseDirectory;
        var path = Path.Combine(dir, "Data", "I18n", $"{lang}.json");
        if (!File.Exists(path))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), JsonOptions);
            return parsed == null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(parsed, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }
}
