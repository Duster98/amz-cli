using System.Runtime.InteropServices;

namespace AmazonScraper.Update;

public static class PlatformInfo
{
    public static string Detect()
    {
        var rid = RuntimeInformation.RuntimeIdentifier;
        if (rid is "linux-x64" or "win-x64" or "win-arm64" or "linux-arm64")
        {
            return rid;
        }

        var os = OperatingSystem.IsWindows()
            ? "win"
            : OperatingSystem.IsLinux()
                ? "linux"
                : OperatingSystem.IsMacOS()
                    ? "osx"
                    : "unknown";
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => "unknown",
        };
        return $"{os}-{architecture}";
    }

    public static bool IsSupported(string platform) =>
        platform is "linux-x64" or "win-x64";

    public static string ExecutableName(string platform) =>
        platform.StartsWith("win-", StringComparison.Ordinal)
            ? "amz-cli.exe"
            : "amz-cli";

    public static string LegacyExecutableName(string platform) =>
        platform.StartsWith("win-", StringComparison.Ordinal)
            ? "amazon.exe"
            : "amazon";
}
