using System.Globalization;
using System.Resources;

namespace MinePack.Core;

public static class LocalizedText
{
    private static readonly ResourceManager Resources = new("MinePack.Core.Resources.Strings", typeof(LocalizedText).Assembly);

    public static CultureInfo SelectUiCulture(CultureInfo systemUiCulture) =>
        CultureInfo.GetCultureInfo(systemUiCulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en");

    public static string Get(string key, params object?[] arguments)
    {
        var value = Resources.GetString(key, CultureInfo.CurrentUICulture)
            ?? throw new InvalidOperationException($"Missing localization resource '{key}' for culture '{CultureInfo.CurrentUICulture.Name}'.");
        return arguments.Length == 0 ? value : string.Format(CultureInfo.CurrentUICulture, value, arguments);
    }
}
