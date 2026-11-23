using AmazonScraper.Core.Monitoring;
using AmazonScraper.Cli.Storage;

namespace AmazonScraper.Cli.Monitoring;

/// <summary>
/// Per-user index of monitor runs. Each entry is a copy of the run metadata;
/// the local pidfile remains the source of truth for --status/--stop.
/// </summary>
public sealed class MonitorRegistry
{
    public MonitorRegistry(string? directory = null)
    {
        DirectoryPath = directory ?? DefaultDirectory();
    }

    public string DirectoryPath { get; }

    public void Register(MonitorPidInfo info)
    {
        Directory.CreateDirectory(DirectoryPath);
        if (string.IsNullOrWhiteSpace(info.Id))
        {
            info.Id = string.IsNullOrWhiteSpace(info.RunId)
                ? $"pid-{info.Pid}"
                : info.RunId;
        }

        var path = Path.Combine(DirectoryPath, $"{SafeName(info.Id)}.json");
        MonitorPidInfo.Save(path, info);
    }

    public bool Unregister(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        try
        {
            var path = Path.Combine(DirectoryPath, $"{SafeName(id)}.json");
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
        catch (Exception)
        {
            // Best effort.
            return false;
        }
    }

    /// <summary>
    /// Removes stale entries that refer to the same logical monitor as
    /// <paramref name="current"/>. A run killed by Task Scheduler, a logoff or
    /// a crash cannot unregister itself; the next run cleans the leftover.
    /// </summary>
    public int Reconcile(MonitorPidInfo current)
    {
        var removed = 0;
        foreach (var entry in ReadAll())
        {
            if (string.Equals(entry.Id, current.Id, StringComparison.Ordinal)
                || !MonitorIdentity.Same(entry, current))
            {
                continue;
            }

            // A different run cannot share our PID while we are alive: the
            // PID was recycled, or an old entry lacks process_started_at.
            if (entry.Pid != current.Pid
                && MonitorProcessInspector.IsRunning(entry))
            {
                continue;
            }

            Unregister(entry.Id);
            removed++;
        }

        return removed;
    }

    public IReadOnlyList<MonitorPidInfo> ReadAll()
    {
        var entries = new List<MonitorPidInfo>();
        if (!Directory.Exists(DirectoryPath))
        {
            return entries;
        }

        foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*.json"))
        {
            try
            {
                var entry = MonitorPidInfo.TryLoad(file);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
            catch (MonitorException)
            {
                // Ignore malformed registry entries.
            }
        }

        return entries;
    }

    public static string DefaultDirectory() =>
        UserStatePaths.MonitorDirectory();

    private static string SafeName(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(
                char.IsLetterOrDigit(character) || character is '-' or '_'
                    ? character
                    : '_');
        }

        return builder.Length == 0 ? "monitor" : builder.ToString();
    }
}
