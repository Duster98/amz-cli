using System.Reflection;

namespace AmazonScraper.Cli;

public static class VersionInfo
{
    public static string Informational { get; } =
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "0.0.0";

    public static string Display { get; } = $"amz-cli {Informational}";
}
