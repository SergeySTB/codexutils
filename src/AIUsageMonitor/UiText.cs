using System.Globalization;

namespace AIUsageMonitor;

internal static class UiText
{
    internal static bool Russian => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";

    internal static string T(string russian, string english) => Russian ? russian : english;
}
