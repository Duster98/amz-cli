using System.Security.Cryptography;
using System.Text;

namespace AmazonScraper.Cli.Startup;

public static class StartupIdentity
{
    public static string CreateId(string savePath, string saveFile)
    {
        var path = NormalizePath(savePath);
        var file = string.IsNullOrWhiteSpace(saveFile)
            ? "amz-monitor"
            : saveFile.Trim();
        if (OperatingSystem.IsWindows())
        {
            path = path.ToLowerInvariant();
            file = file.ToLowerInvariant();
        }

        var key = $"{path}\0{file}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash.AsSpan(0, 6)).ToLowerInvariant();
    }

    public static string NormalizePath(string path)
    {
        var full = Path.GetFullPath(path.Trim());
        return Path.TrimEndingDirectorySeparator(full);
    }

    public static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(
                NormalizePath(left),
                NormalizePath(right),
                comparison);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool FilesEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(
            left.Trim(),
            right.Trim(),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }
}
