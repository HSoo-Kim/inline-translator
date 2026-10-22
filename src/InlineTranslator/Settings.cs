using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InlineTranslator;

[JsonConverter(typeof(JsonStringEnumConverter<Provider>))]
public enum Provider { None, Claude, Codex }

public sealed record EngineChoice(Provider Provider, string Model)
{
    public string DisplayName => Provider == Provider.None ? "" :
        (Provider == Provider.Claude ? "Claude · " : "Codex · ")
        + (EngineCli.SuggestedModels(Provider).FirstOrDefault(option => option.Id == Model)?.Label ?? Model);
}

public sealed record HotkeyPreset(bool Enabled, string Hotkey, string TargetLanguage);

public sealed record AppSettings
{
    public const int PresetCount = 3;
    public static readonly string[] UiLanguages = ["ko", "en", "zh-CN", "ja"];
    public static readonly string[] Tones = ["natural", "casual", "formal"];
    public static readonly string[] BubbleThemes = ["light", "dark"];
    public const string DefaultClaudeModel = "claude-haiku-5-5";
    public const string DefaultCodexModel = "gpt-5.6-luna";

    public static readonly IReadOnlyList<HotkeyPreset> DefaultPresets =
    [
        new(true, "Ctrl+Alt+T", "en"),
        new(false, "Ctrl+Alt+J", "ja"),
        new(false, "Ctrl+Alt+K", "ko"),
    ];

    public static readonly IReadOnlyList<string> DefaultWholeInputApps =
        ["Telegram", "KakaoTalk", "Slack", "Discord", "WhatsApp", "LINE", "ms-teams", "Teams", "Signal"];

    public string UiLanguage { get; init; } = "";
    public EngineChoice Primary { get; init; } = new(Provider.Claude, DefaultClaudeModel);
    public EngineChoice Fallback { get; init; } = new(Provider.Codex, DefaultCodexModel);
    public IReadOnlyList<HotkeyPreset> Presets { get; init; } = DefaultPresets;
    public string Tone { get; init; } = "natural";
    public IReadOnlyList<string> WholeInputApps { get; init; } = DefaultWholeInputApps;
    public string BubbleTheme { get; init; } = "light";
    public bool CheckForUpdates { get; init; } = true;
    public bool AutoInstallUpdates { get; init; } = true;

    public static string DefaultModel(Provider provider) => provider switch
    {
        Provider.Claude => DefaultClaudeModel,
        Provider.Codex => DefaultCodexModel,
        _ => "",
    };

    public static bool IsValidModel(string? model) =>
        !string.IsNullOrWhiteSpace(model) && model.Length <= 100 && model.Trim().All(c =>
            char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or ':' or '/' or '[' or ']');

    public AppSettings Normalize()
    {
        var presets = new List<HotkeyPreset>(PresetCount);
        var used = new HashSet<Hotkey>();
        for (int i = 0; i < PresetCount; i++)
        {
            var preset = i < (Presets?.Count ?? 0) ? Presets![i] : null;
            preset ??= DefaultPresets[i] with { Enabled = false };
            string language = Languages.Find(preset.TargetLanguage)?.Code ?? "en";
            bool valid = InlineTranslator.Hotkey.TryParse(preset.Hotkey, out var hotkey);
            bool enabled = preset.Enabled && valid && used.Add(hotkey);
            presets.Add(new HotkeyPreset(enabled, valid ? hotkey.ToString() : "", language));
        }

        var apps = (WholeInputApps ?? [])
            .Select(NormalizeAppName)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToArray();

        var primary = NormalizeEngine(Primary, allowNone: false) ?? new EngineChoice(Provider.Claude, DefaultClaudeModel);
        var fallback = NormalizeEngine(Fallback, allowNone: true) ?? new EngineChoice(Provider.None, "");
        if (fallback == primary) fallback = new EngineChoice(Provider.None, "");

        return this with
        {
            UiLanguage = UiLanguages.FirstOrDefault(code => code.Equals(UiLanguage, StringComparison.OrdinalIgnoreCase)) ?? "",
            Primary = primary,
            Fallback = fallback,
            Presets = presets,
            Tone = Tones.Contains(Tone) ? Tone : "natural",
            BubbleTheme = BubbleThemes.Contains(BubbleTheme) ? BubbleTheme : "light",
            WholeInputApps = apps,
        };
    }

    public static string NormalizeAppName(string? name)
    {
        string trimmed = (name ?? "").Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[..^4];
        return trimmed.Length <= 64 && trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 ? trimmed : "";
    }

    private static EngineChoice? NormalizeEngine(EngineChoice? engine, bool allowNone)
    {
        if (engine is null || !Enum.IsDefined(engine.Provider)) return null;
        if (engine.Provider == Provider.None) return allowNone ? new EngineChoice(Provider.None, "") : null;
        string model = IsValidModel(engine.Model) ? engine.Model.Trim() : DefaultModel(engine.Provider);
        return new EngineChoice(engine.Provider, model);
    }
}

public static class SettingsStore
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InlineTranslator", "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path, Encoding.UTF8), Options);
            return (settings ?? new AppSettings()).Normalize();
        }
        catch (JsonException)
        {
            return new AppSettings().Normalize();
        }
    }

    public static void Save(string path, AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings.Normalize(), Options), new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
