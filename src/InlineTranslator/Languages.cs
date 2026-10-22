namespace InlineTranslator;

public sealed record TargetLanguage(string Code, string EnglishName, string NativeName)
{
    public override string ToString() => NativeName == EnglishName ? EnglishName : $"{NativeName} ({EnglishName})";
}

public static class Languages
{
    public static readonly IReadOnlyList<TargetLanguage> All =
    [
        new("en", "English", "English"),
        new("ko", "Korean", "한국어"),
        new("ja", "Japanese", "日本語"),
        new("zh-CN", "Simplified Chinese", "简体中文"),
        new("zh-TW", "Traditional Chinese", "繁體中文"),
        new("es", "Spanish", "Español"),
        new("fr", "French", "Français"),
        new("de", "German", "Deutsch"),
        new("pt", "Portuguese", "Português"),
        new("it", "Italian", "Italiano"),
        new("ru", "Russian", "Русский"),
        new("vi", "Vietnamese", "Tiếng Việt"),
        new("th", "Thai", "ไทย"),
        new("id", "Indonesian", "Bahasa Indonesia"),
        new("ar", "Arabic", "العربية"),
        new("hi", "Hindi", "हिन्दी"),
        new("tr", "Turkish", "Türkçe"),
        new("nl", "Dutch", "Nederlands"),
        new("pl", "Polish", "Polski"),
    ];

    public static TargetLanguage? Find(string? code) =>
        All.FirstOrDefault(language => language.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
}
