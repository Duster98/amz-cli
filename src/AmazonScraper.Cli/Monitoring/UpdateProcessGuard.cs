using System.Diagnostics;

namespace AmazonScraper.Cli.Monitoring;

/// <summary>
/// Before replacing a single-file executable, no other process may be running
/// from the same installation. This closes the race where a second CLI command
/// starts while an update is being applied.
/// </summary>
public static class UpdateProcessGuard
{
    public static async Task<IReadOnlyList<int>> WaitForOtherProcessesAsync(
        string installDir,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var running = FindOtherProcesses(installDir);
            if (running.Count == 0 || DateTimeOffset.UtcNow >= deadline)
            {
                return running;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }
    }

    private static IReadOnlyList<int> FindOtherProcesses(string installDir)
    {
        var result = new List<int>();
        var installation = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(installDir));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (var process in Process.GetProcessesByName("amz-cli"))
        {
            try
            {
                if (process.Id == Environment.ProcessId)
                {
                    continue;
                }

                var path = process.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                var directory = Path.GetDirectoryName(Path.GetFullPath(path));
                if (directory is not null
                    && string.Equals(
                        Path.TrimEndingDirectorySeparator(directory),
                        installation,
                        comparison))
                {
                    result.Add(process.Id);
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

        return result;
    }
}
