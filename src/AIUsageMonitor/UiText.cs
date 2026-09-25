using System;
using System.Globalization;

namespace AIUsageMonitor;

public static class UiText
{
    private static volatile string language = "system";

    public static void SetLanguage(string value)
    {
        if (value is not ("system" or "ru" or "en")) throw new ArgumentException("Unsupported UI language.", nameof(value));
        language = value;
    }

    internal static bool Russian => language == "ru" ||
        (language == "system" && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru");

    internal static string T(string russian, string english) => Russian ? russian : english;
}
