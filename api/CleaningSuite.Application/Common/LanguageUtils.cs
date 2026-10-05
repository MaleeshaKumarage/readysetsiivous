namespace CleaningSuite.Application.Common;

public static class LanguageUtils
{
    public static string SanitizeLang(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang))
            return "fi";

        var primarySubtag = lang.Split('-', '_')[0];
        var clean = new string(primarySubtag.Where(char.IsLetterOrDigit).Take(5).ToArray()).ToLowerInvariant();

        return string.IsNullOrEmpty(clean) ? "fi" : clean;
    }
}
