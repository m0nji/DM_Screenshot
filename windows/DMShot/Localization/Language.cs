namespace DMShot.Localization;

public enum Language { English, German }

public static class LanguageExtensions
{
    public static string Code(this Language l) => l == Language.German ? "de" : "en";
    public static string DisplayName(this Language l) => l == Language.German ? "Deutsch" : "English";
}

public static class LanguageCodes
{
    /// <summary>Maps a stored code to a Language; unknown/null falls back to English.</summary>
    public static Language FromCode(string? code) => code == "de" ? Language.German : Language.English;
}
