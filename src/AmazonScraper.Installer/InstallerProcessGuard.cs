using System.Diagnostics;
using AmazonScraper.Update;

namespace AmazonScraper.Installer;

public static class InstallerProcessGuard
{
    public static void EnsureNotRunning(string installDir)
    {
        var root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(installDir));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var rootWithSeparator = root + Path.DirectorySeparatorChar;
        var found = new List<int>();

        foreach (var name in new[] { "amz-cli", "amz-notify" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    var full = Path.GetFullPath(path);
                    if (full.StartsWith(rootWithSeparator, comparison)
                        || string.Equals(
                            Path.TrimEndingDirectorySeparator(full),
                            root,
                            comparison))
                    {
                        found.Add(process.Id);
                    }
                }
                catch (Exception)
                {
                    // Access can be denied for another user's process.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        if (found.Count > 0)
        {
            throw new UpdateException(
                $"There are active amz-cli processes in '{installDir}' " +
                $"(PID {string.Join(", ", found)}). Fermali e riprova.");
        }
    }
}
