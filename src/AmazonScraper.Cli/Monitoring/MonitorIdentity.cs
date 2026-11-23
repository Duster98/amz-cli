using AmazonScraper.Cli.Startup;

namespace AmazonScraper.Cli.Monitoring;

/// <summary>
/// Identifies the logical monitor behind a run. A restart creates a new run id
/// and pid, but the startup id or the save path/file stays the same.
/// </summary>
public static class MonitorIdentity
{
    public static bool Same(MonitorPidInfo left, MonitorPidInfo right)
    {
        if (!string.IsNullOrWhiteSpace(left.StartupId)
            && !string.IsNullOrWhiteSpace(right.StartupId))
        {
            return string.Equals(
                left.StartupId,
                right.StartupId,
                StringComparison.Ordinal);
        }

        if (StartupIdentity.PathsEqual(left.SavePath, right.SavePath)
            && StartupIdentity.FilesEqual(left.SaveFile, right.SaveFile))
        {
            return true;
        }

        return StartupIdentity.PathsEqual(left.PidFile, right.PidFile);
    }
}
