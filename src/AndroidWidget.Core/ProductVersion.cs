using System.Reflection;

namespace AndroidWidget.Core;

public static class ProductVersion
{
    public static string Number { get; } = ResolveNumber();

    public static string Label { get; } = $"v{Number}";

    public static string ProductLabel { get; } = $"Device Widget · {Label}";

    private static string ResolveNumber()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(ProductVersion).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
            return informationalVersion.Split('+', 2)[0];

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
