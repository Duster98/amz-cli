namespace AmazonScraper.Update;

/// <summary>
/// Per-user state used by the updater. It intentionally follows the same
/// directories as the monitor registry so Linux and Windows keep one state
/// root.
/// </summary>
public static class UpdatePaths
{
    public static string Root()
    {
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            return string.IsNullOrWhiteSpace(local)
                ? Path.Combine(Path.GetTempPath(), "AmzCLI")
                : Path.Combine(local, "AmzCLI");
        }

        if (OperatingSystem.IsLinux())
        {
            var stateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
            if (!string.IsNullOrWhiteSpace(stateHome))
            {
                return Path.Combine(stateHome, "amz-cli");
            }

            var home = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
            {
                return Path.Combine(home, ".local", "state", "amz-cli");
            }
        }

        return Path.Combine(Path.GetTempPath(), "amz-cli");
    }

    public static string UpdatesDirectory() => Path.Combine(Root(), "updates");

    public static string DownloadsDirectory() =>
        Path.Combine(UpdatesDirectory(), "downloads");

    public static string StagingDirectory() =>
        Path.Combine(UpdatesDirectory(), "staging");

    public static string BackupsDirectory() =>
        Path.Combine(UpdatesDirectory(), "backups");

    public static string PlansDirectory() =>
        Path.Combine(UpdatesDirectory(), "plans");

    public static string HelpersDirectory() =>
        Path.Combine(UpdatesDirectory(), "helpers");

    public static string JournalPath() =>
        Path.Combine(UpdatesDirectory(), "journal.json");

    public static string ConfigPath() =>
        Path.Combine(UpdatesDirectory(), "config.json");

    public static string LastCheckPath() =>
        Path.Combine(UpdatesDirectory(), "last-check.json");

    public static string AutoLockPath() =>
        Path.Combine(UpdatesDirectory(), "auto.lock");

    public static string MaintenancePath() =>
        Path.Combine(UpdatesDirectory(), "maintenance.lock");

    public static string LockPath() =>
        Path.Combine(UpdatesDirectory(), "update.lock");

    public static string LogPath() =>
        Path.Combine(UpdatesDirectory(), "update.log");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(UpdatesDirectory());
        Directory.CreateDirectory(DownloadsDirectory());
        Directory.CreateDirectory(StagingDirectory());
        Directory.CreateDirectory(BackupsDirectory());
        Directory.CreateDirectory(PlansDirectory());
        Directory.CreateDirectory(HelpersDirectory());
    }

    /// <summary>
    /// Dot-prefixed names are hidden on Unix but not in Windows Explorer.
    /// Apply the Hidden attribute to temporary staging/rollback directories.
    /// </summary>
    public static void MarkHidden(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var info = new DirectoryInfo(path);
            if (info.Exists)
            {
                info.Attributes |= FileAttributes.Hidden;
            }
        }
        catch (Exception)
        {
            // Best effort: the directory stays usable even without the flag.
        }
    }

    /// <summary>
    /// Sibling directories created for the atomic file swap. They live next
    /// to the installation because renames must stay on the same volume.
    /// </summary>
    public static IReadOnlyList<string> ListStaleSiblingDirectories(
        string installDir,
        UpdateJournal? journal = null,
        TimeSpan? minimumAge = null)
    {
        var result = new List<string>();
        var fullInstall = Path.GetFullPath(installDir);
        var parent = Path.GetDirectoryName(fullInstall);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
        {
            return result;
        }

        List<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(parent).ToList();
        }
        catch (Exception)
        {
            return result;
        }

        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddActive(journal?.StagingDir);
        AddActive(journal?.RollbackDir);
        var cutoff = DateTime.UtcNow - (minimumAge ?? TimeSpan.Zero);

        foreach (var directory in directories)
        {
            var name = Path.GetFileName(directory);
            if (!IsSiblingTempName(name))
            {
                continue;
            }

            var full = Path.GetFullPath(directory);
            if (string.Equals(full, fullInstall, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (active.Contains(full))
            {
                continue;
            }

            if (minimumAge is { } age && age > TimeSpan.Zero)
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(directory) > cutoff)
                    {
                        continue;
                    }
                }
                catch (Exception)
                {
                    continue;
                }
            }

            result.Add(full);
        }

        return result;

        void AddActive(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                active.Add(Path.GetFullPath(path));
            }
        }
    }

    public static int CleanupStaleSiblingDirectories(
        string installDir,
        UpdateJournal? journal = null,
        TimeSpan? minimumAge = null)
    {
        var removed = 0;
        foreach (var directory in ListStaleSiblingDirectories(
                     installDir,
                     journal,
                     minimumAge))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                removed++;
            }
            catch (Exception)
            {
                // Locked by an antivirus/helper: retry on the next run.
            }
        }

        return removed;
    }

    private static bool IsSiblingTempName(string name) =>
        name.StartsWith(".amz-cli-update-", StringComparison.Ordinal)
        || name.StartsWith(".amz-cli-restore-", StringComparison.Ordinal);
}
