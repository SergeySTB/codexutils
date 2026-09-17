using System.Reflection;

namespace CodexLimits;

public static class ProductInfo
{
    public static string Version => typeof(ProductInfo).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?.Split('+')[0] ?? "unknown";
}
