using System.IO;
using System.Reflection;

namespace AIUsageMonitor;

public static class ProductInfo
{
    public static string GetDefaultConfigPath(string localAppData)
    {
        var current = Path.Combine(localAppData, "AIUsageMonitor", "config.json");
        var legacy = Path.Combine(localAppData, "CodexLimits", "config.json");
        return !File.Exists(current) && File.Exists(legacy) ? legacy : current;
    }

    public static string Version => typeof(ProductInfo).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?.Split('+')[0] ?? "unknown";
}
