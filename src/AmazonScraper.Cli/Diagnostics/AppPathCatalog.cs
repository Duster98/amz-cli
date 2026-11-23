using AmazonScraper.Cli.Monitoring;
using AmazonScraper.Cli.Startup;
using AmazonScraper.Cli.Storage;
using AmazonScraper.Core.Monitoring;
using AmazonScraper.Update;

namespace AmazonScraper.Cli.Diagnostics;

public sealed class AppPathCatalogOptions
{
    public bool IncludeMissing { get; init; }

    public bool IncludeInstall { get; init; }

    public IReadOnlyList<string> AdditionalSavePaths { get; init; } = [];

    public string? StateRoot { get; init; }

    public string? InstallDir { get; init; }

    public string? MonitorRegistryDirectory { get; init; }

    public string? StartupRegistryDirectory { get; init; }

    public string? SystemdDirectory { get; init; }

    public bool? IsWindows { get; init; }
}

/// <summary>
/// Builds the catalog of paths used by the CLI. It only reads metadata and the
/// existing monitor/startup registries; it never prints file contents or
/// command-line arguments, which may contain cookies.
/// </summary>
public static class AppPathCatalog
{
    private static readonly string[] UpdateFiles =
    [
        "config.json",
        "last-check.json",
        "journal.json",
        "update.log",
        "auto.lock",
        "maintenance.lock",
        "update.lock",
    ];

    private static readonly string[] UpdateDirectories =
    [
        "downloads",
        "staging",
        "backups",
        "plans",
        "helpers",
    ];

    public static AppPathCatalogResult Build(AppPathCatalogOptions options)
    {
        var windows = options.IsWindows ?? OperatingSystem.IsWindows();
        var stateRoot = FullPath(options.StateRoot ?? UserStatePaths.Root());
        var installDir = FullPath(
            options.InstallDir ?? InstallLocation.Resolve());
        var monitorDirectory = options.MonitorRegistryDirectory
            ?? Path.Combine(stateRoot, "monitors");
        var startupDirectory = options.StartupRegistryDirectory
            ?? Path.Combine(stateRoot, "startups");

        var groups = new List<AppPathGroup>
        {
            BuildInstallGroup(installDir, windows, options.IncludeInstall),
            BuildStateGroup(
                stateRoot,
                installDir,
                windows,
                options.IncludeMissing),
        };

        var monitors = new MonitorRegistry(monitorDirectory).ReadAll();
        var startups = new StartupRegistry(startupDirectory).ReadAll();
        groups.AddRange(
            BuildMonitorGroups(
                monitors,
                startups,
                options.AdditionalSavePaths,
                options.IncludeMissing));
        groups.Add(BuildExternalRegistrations(
            startups,
            windows,
            options.SystemdDirectory,
            options.IncludeMissing));

        return new AppPathCatalogResult
        {
            Os = windows ? "win-x64" : "linux-x64",
            StateRoot = stateRoot,
            InstallDir = installDir,
            GeneratedAt = DateTimeOffset.UtcNow.ToString("O"),
            Groups = groups,
            Summary = Summarize(groups),
        };
    }

    private static AppPathGroup BuildInstallGroup(
        string installDir,
        bool windows,
        bool includeAll)
    {
        var entries = new List<AppPathEntry>();
        AddPath(
            entries,
            installDir,
            AppPathKind.Directory,
            "install",
            description: "Executable directory",
            required: true,
            includeMissing: true);

        var executable = Path.Combine(
            installDir,
            windows ? "amz-cli.exe" : "amz-cli");
        AddPath(
            entries,
            executable,
            AppPathKind.File,
            "install",
            description: "Main executable",
            required: true,
            includeMissing: true);

        if (windows)
        {
            AddPath(
                entries,
                Path.Combine(installDir, "amz-notify.exe"),
                AppPathKind.File,
                "install",
                description: "Windows notification helper",
                includeMissing: true);
        }

        if (includeAll)
        {
            foreach (var entry in EnumerateInstallEntries(installDir))
            {
                AddPath(
                    entries,
                    entry,
                    Directory.Exists(entry)
                        ? AppPathKind.Directory
                        : AppPathKind.File,
                    "install",
                    description: "Installation file");
            }
        }

        return new AppPathGroup
        {
            Id = "install",
            Title = "Installation",
            BasePath = installDir,
            Description = installDir,
            Entries = entries,
        };
    }

    private static AppPathGroup BuildStateGroup(
        string stateRoot,
        string installDir,
        bool windows,
        bool includeMissing)
    {
        var entries = new List<AppPathEntry>();
        var monitors = Path.Combine(stateRoot, "monitors");
        var startups = Path.Combine(stateRoot, "startups");
        var updates = Path.Combine(stateRoot, "updates");
        var web = Path.Combine(stateRoot, "web");
        var transport = Path.Combine(stateRoot, "transport");

        AddPath(
            entries,
            stateRoot,
            AppPathKind.Directory,
            "state",
            description: "Per-user state root",
            required: true,
            includeMissing: true);
        AddPath(
            entries,
            monitors,
            AppPathKind.Directory,
            "state",
            description: "Monitor registry",
            includeMissing: includeMissing);
        AddPath(
            entries,
            startups,
            AppPathKind.Directory,
            "state",
            description: "Auto-start definitions",
            includeMissing: includeMissing);
        AddPath(
            entries,
            updates,
            AppPathKind.Directory,
            "updates",
            description: "Updater state",
            includeMissing: includeMissing);
        AddPath(
            entries,
            transport,
            AppPathKind.Directory,
            "state",
            description: "HTTP transport cooldown per marketplace",
            includeMissing: includeMissing);
        AddPath(
            entries,
            web,
            AppPathKind.Directory,
            "state",
            description: "Web session, UI configuration, and logs",
            includeMissing: includeMissing);
        AddPath(
            entries,
            Path.Combine(stateRoot, "library.json"),
            AppPathKind.File,
            "state",
            description: "Saved products and groups shared by CLI and Web",
            includeMissing: includeMissing);
        AddPath(
            entries,
            Path.Combine(stateRoot, "library.json.lock"),
            AppPathKind.File,
            "state",
            description: "Saved products library lock",
            includeMissing: includeMissing);
        AddPath(
            entries,
            Path.Combine(stateRoot, "monitor-bindings.json"),
            AppPathKind.File,
            "state",
            description: "Monitor bindings for saved products and groups",
            includeMissing: includeMissing);
        AddPath(
            entries,
            Path.Combine(stateRoot, "monitor-bindings.json.lock"),
            AppPathKind.File,
            "state",
            description: "Monitor bindings lock",
            includeMissing: includeMissing);
        AddPath(
            entries,
            Path.Combine(stateRoot, "history.json"),
            AppPathKind.File,
            "state",
            description: "Searches and opened products history",
            includeMissing: includeMissing);
        AddPath(
            entries,
            Path.Combine(stateRoot, "history.json.lock"),
            AppPathKind.File,
            "state",
            description: "History lock",
            includeMissing: includeMissing);
        AddPath(
            entries,
            Path.Combine(stateRoot, "http-settings.json"),
            AppPathKind.File,
            "state",
            description: "Persistent HTTP settings shared with the Web UI",
            includeMissing: includeMissing);
        AddPath(
            entries,
            Path.Combine(stateRoot, "web", "compare.json"),
            AppPathKind.File,
            "state",
            description: "Products in the Web comparison tabs",
            includeMissing: includeMissing);
        AddPath(
            entries,
            Path.Combine(stateRoot, "web", "compare.json.lock"),
            AppPathKind.File,
            "state",
            description: "Comparison lock",
            includeMissing: includeMissing);
        AddPath(
            entries,
            Path.Combine(web, "history-cache"),
            AppPathKind.Directory,
            "state",
            description: "Session cache for search history",
            includeMissing: includeMissing);
        AddPath(
            entries,
            Path.Combine(web, "product-cache"),
            AppPathKind.Directory,
            "state",
            description: "Session cache for opened products",
            includeMissing: includeMissing);
        AddPath(
            entries,
            Path.Combine(web, "product-cache.lock"),
            AppPathKind.File,
            "state",
            description: "Opened products cache lock",
            includeMissing: includeMissing);
        AddPath(
            entries,
            Path.Combine(web, "cache-settings.json"),
            AppPathKind.File,
            "state",
            description: "Product cache size configuration",
            includeMissing: includeMissing);

        AddDirectoryFiles(
            entries,
            monitors,
            "monitor-registry",
            includeMissing);
        AddDirectoryFiles(
            entries,
            startups,
            "startup-registry",
            includeMissing);
        AddDirectoryFiles(
            entries,
            web,
            "web",
            includeMissing);

        foreach (var name in UpdateDirectories)
        {
            AddPath(
                entries,
                Path.Combine(updates, name),
                AppPathKind.Directory,
                "updates",
                description: $"Updater directory: {name}",
                includeMissing: includeMissing);
        }
        foreach (var name in UpdateFiles)
        {
            AddPath(
                entries,
                Path.Combine(updates, name),
                AppPathKind.File,
                "updates",
                description: $"Updater file: {name}",
                includeMissing: includeMissing);
        }

        if (windows)
        {
            var notifications = Path.Combine(stateRoot, "notifications");
            AddPath(
                entries,
                notifications,
                AppPathKind.Directory,
                "notifications",
                description: "Windows notification payloads",
                includeMissing: includeMissing);
            AddDirectoryFiles(
                entries,
                notifications,
                "notifications",
                includeMissing);
            AddPath(
                entries,
                Path.Combine(stateRoot, "startup-launch.log"),
                AppPathKind.File,
                "startup",
                description: "Windows launcher log",
                includeMissing: includeMissing);
        }
        foreach (var directory in UpdatePaths
                     .ListStaleSiblingDirectories(installDir)
                     .Where(Directory.Exists))
        {
            AddPath(
                entries,
                directory,
                AppPathKind.Directory,
                "update-temp",
                description: "Temporary update/rollback directory",
                includeMissing: false,
                statusOverride: "stale");
        }

        return new AppPathGroup
        {
            Id = "state",
            Title = "Application state",
            BasePath = stateRoot,
            Description = stateRoot,
            Entries = entries,
        };
    }

    private static IEnumerable<AppPathGroup> BuildMonitorGroups(
        IReadOnlyList<MonitorPidInfo> monitors,
        IReadOnlyList<StartupDefinition> startups,
        IReadOnlyList<string> additionalSavePaths,
        bool includeMissing)
    {
        var descriptors = new Dictionary<string, MonitorDescriptor>(
            StringComparer.Ordinal);

        foreach (var info in monitors)
        {
            var savePath = info.SavePath;
            if (string.IsNullOrWhiteSpace(savePath))
            {
                savePath = string.IsNullOrWhiteSpace(info.PidFile)
                    ? Environment.CurrentDirectory
                    : Path.GetDirectoryName(info.PidFile)
                        ?? Environment.CurrentDirectory;
            }
            var saveFile = info.SaveFile;
            if (string.IsNullOrWhiteSpace(saveFile))
            {
                saveFile = string.IsNullOrWhiteSpace(info.PidFile)
                    ? "amz-monitor"
                    : Path.GetFileNameWithoutExtension(info.PidFile);
            }

            var key = MonitorKey(savePath, saveFile);
            descriptors[key] = new MonitorDescriptor(
                key,
                FullPath(savePath),
                saveFile!,
                info.Id,
                MonitorProcessInspector.IsRunning(info)
                    ? "running"
                    : "stale",
                info.Mode);
        }

        foreach (var savePath in additionalSavePaths)
        {
            if (string.IsNullOrWhiteSpace(savePath) || !Directory.Exists(savePath))
            {
                continue;
            }

            foreach (var pidFile in Directory.EnumerateFiles(
                         savePath,
                         "*.pid",
                         SearchOption.TopDirectoryOnly))
            {
                MonitorPidInfo? info;
                try
                {
                    info = MonitorPidInfo.TryLoad(pidFile);
                }
                catch (MonitorException)
                {
                    continue;
                }
                if (info is null)
                {
                    continue;
                }

                var resolvedPath = info.SavePath ?? Path.GetDirectoryName(pidFile)
                    ?? savePath;
                var resolvedFile = info.SaveFile
                    ?? Path.GetFileNameWithoutExtension(pidFile);
                var key = MonitorKey(resolvedPath, resolvedFile);
                descriptors.TryAdd(
                    key,
                    new MonitorDescriptor(
                        key,
                        FullPath(resolvedPath),
                        resolvedFile,
                        info.Id,
                        MonitorProcessInspector.IsRunning(info)
                            ? "running"
                            : "stale",
                        info.Mode));
            }
        }

        foreach (var startup in startups)
        {
            var savePath = startup.SavePath;
            var saveFile = string.IsNullOrWhiteSpace(startup.SaveFile)
                ? "amz-monitor"
                : startup.SaveFile;
            if (string.IsNullOrWhiteSpace(savePath))
            {
                continue;
            }

            var key = MonitorKey(savePath, saveFile);
            descriptors.TryAdd(
                key,
                new MonitorDescriptor(
                    key,
                    FullPath(savePath),
                    saveFile,
                    startup.Id,
                    "configured",
                    "startup"));
        }

        var groups = new List<AppPathGroup>();
        foreach (var descriptor in descriptors.Values
                     .OrderBy(item => item.SavePath, StringComparer.Ordinal)
                     .ThenBy(item => item.SaveFile, StringComparer.Ordinal))
        {
            MonitorPaths paths;
            try
            {
                paths = MonitorPaths.Resolve(
                    descriptor.SavePath,
                    descriptor.SaveFile);
            }
            catch (MonitorException)
            {
                continue;
            }

            var entries = new List<AppPathEntry>();
            AddPath(
                entries,
                paths.SavePath,
                AppPathKind.Directory,
                "monitor-data",
                descriptor.Id,
                "Monitor data directory",
                required: true,
                includeMissing: includeMissing);
            AddPath(
                entries,
                paths.StatePath,
                AppPathKind.File,
                "monitor-data",
                descriptor.Id,
                "Monitor state and budget",
                includeMissing: includeMissing);
            AddPath(
                entries,
                paths.PidPath,
                AppPathKind.File,
                "monitor-data",
                descriptor.Id,
                "Monitor pidfile",
                includeMissing: includeMissing,
                statusOverride: descriptor.Status == "stale"
                    ? "stale"
                    : null);
            AddPath(
                entries,
                paths.LogPath,
                AppPathKind.File,
                "monitor-data",
                descriptor.Id,
                "Monitor log",
                includeMissing: includeMissing);
            AddPath(
                entries,
                paths.LockPath,
                AppPathKind.File,
                "monitor-data",
                descriptor.Id,
                "Monitor lock",
                includeMissing: includeMissing);

            var dataFiles = FindDataFiles(descriptor, monitors, startups);
            foreach (var dataFile in dataFiles)
            {
                AddPath(
                    entries,
                    dataFile,
                    AppPathKind.File,
                    "monitor-data",
                    descriptor.Id,
                    "Observations/alerts file",
                    includeMissing: includeMissing);
            }

            AddPath(
                entries,
                paths.PerGroupRoot,
                AppPathKind.Directory,
                "monitor-data",
                descriptor.Id,
                "Per-group directory",
                includeMissing: false);

            groups.Add(new AppPathGroup
            {
                Id = $"monitor:{descriptor.Key.Replace('\0', ':')}",
                Title = $"Monitor {descriptor.SaveFile}",
                BasePath = paths.SavePath,
                Description = $"{descriptor.Status} · {descriptor.Mode ?? "foreground"}",
                Status = descriptor.Status,
                Entries = entries,
            });
        }

        return groups;
    }

    private static AppPathGroup BuildExternalRegistrations(
        IReadOnlyList<StartupDefinition> startups,
        bool windows,
        string? systemdDirectory,
        bool includeMissing)
    {
        var entries = new List<AppPathEntry>();
        var basePath = windows
            ? string.Empty
            : systemdDirectory ?? UserStatePaths.SystemdUserDirectory();
        if (!windows && !string.IsNullOrWhiteSpace(basePath))
        {
            basePath = FullPath(basePath);
        }

        foreach (var startup in startups)
        {
            if (windows)
            {
                AddExternal(
                    entries,
                    $"AmzCLI-{startup.Id}",
                    $"Task Scheduler: {startup.Name}",
                    "configured");
            }
            else if (!string.IsNullOrWhiteSpace(basePath))
            {
                var unitPath = Path.Combine(
                    basePath,
                    $"amz-monitor-{startup.Id}.service");
                var exists = File.Exists(unitPath);
                if (!exists && !includeMissing)
                {
                    continue;
                }

                AddExternal(
                    entries,
                    unitPath,
                    $"systemd user: {startup.Name}",
                    exists ? "ok" : "missing");
            }
        }

        return new AppPathGroup
        {
            Id = "external",
            Title = "External registrations",
            BasePath = basePath ?? string.Empty,
            Description = windows
                ? "Task Scheduler"
                : "systemd user / environments",
            Entries = entries,
        };
    }

    private static IReadOnlyList<string> FindDataFiles(
        MonitorDescriptor descriptor,
        IReadOnlyList<MonitorPidInfo> monitors,
        IReadOnlyList<StartupDefinition> startups)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var info in monitors.Where(info =>
                     string.Equals(
                         MonitorKey(
                             info.SavePath ?? string.Empty,
                             info.SaveFile ?? string.Empty),
                         descriptor.Key,
                         StringComparison.Ordinal)))
        {
            foreach (var file in info.DataFiles)
            {
                result.Add(FullPath(file));
            }
        }

        foreach (var startup in startups.Where(startup =>
                     string.Equals(
                         MonitorKey(
                             startup.SavePath,
                             startup.SaveFile),
                         descriptor.Key,
                         StringComparison.Ordinal)))
        {
            foreach (var file in startup.DataFiles)
            {
                result.Add(FullPath(file));
            }
        }

        return result.ToArray();
    }

    private static void AddDirectoryFiles(
        List<AppPathEntry> entries,
        string directory,
        string category,
        bool includeMissing)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(
                     directory,
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            AddPath(
                entries,
                file,
                AppPathKind.File,
                category,
                description: Path.GetFileName(file),
                includeMissing: includeMissing);
        }
    }

    private static void AddExternal(
        List<AppPathEntry> entries,
        string path,
        string description,
        string status)
    {
        entries.Add(new AppPathEntry
        {
            Path = path,
            Kind = "external",
            Category = "external",
            Description = description,
            Exists = status == "ok",
            Status = status,
        });
    }

    private static void AddPath(
        List<AppPathEntry> entries,
        string path,
        AppPathKind kind,
        string category,
        string? groupId = null,
        string? description = null,
        bool required = false,
        bool includeMissing = false,
        string? statusOverride = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var fullPath = FullPath(path);
        if (entries.Any(entry =>
                string.Equals(entry.Path, fullPath, StringComparison.Ordinal)
                && entry.Kind == KindName(kind)))
        {
            return;
        }

        var exists = kind switch
        {
            AppPathKind.File => File.Exists(fullPath),
            AppPathKind.Directory => Directory.Exists(fullPath),
            _ => false,
        };
        if (!exists && !includeMissing)
        {
            return;
        }

        long? size = null;
        DateTimeOffset? modified = null;
        var status = statusOverride ?? (exists ? "ok" : "missing");
        if (exists)
        {
            try
            {
                if (kind == AppPathKind.File)
                {
                    var info = new FileInfo(fullPath);
                    size = info.Length;
                    modified = info.LastWriteTimeUtc;
                }
                else if (kind == AppPathKind.Directory)
                {
                    var info = new DirectoryInfo(fullPath);
                    modified = info.LastWriteTimeUtc;
                    if (!info.EnumerateFileSystemInfos().Any())
                    {
                        status = "empty";
                    }
                }
            }
            catch (Exception)
            {
                status = "unreadable";
            }
        }

        entries.Add(new AppPathEntry
        {
            Path = fullPath,
            Kind = KindName(kind),
            Category = category,
            GroupId = groupId,
            Description = description,
            Exists = exists,
            Size = size,
            ModifiedAt = modified,
            Status = status,
            Required = required,
        });
    }

    private static IEnumerable<string> EnumerateInstallEntries(string installDir)
    {
        if (!Directory.Exists(installDir))
        {
            yield break;
        }

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(
                installDir,
                "*",
                SearchOption.AllDirectories).Take(2_000);
        }
        catch (Exception)
        {
            yield break;
        }

        foreach (var entry in entries)
        {
            try
            {
                var info = new FileInfo(entry);
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }
            }
            catch (Exception)
            {
                // Best effort: still list the entry.
            }

            yield return entry;
        }
    }

    private static AppPathSummary Summarize(IEnumerable<AppPathGroup> groups)
    {
        var summary = new AppPathSummary();
        foreach (var entry in groups.SelectMany(group => group.Entries))
        {
            switch (entry.Kind)
            {
                case "file":
                    summary.Files++;
                    break;
                case "directory":
                    summary.Directories++;
                    break;
                case "external":
                    summary.External++;
                    break;
            }

            switch (entry.Status)
            {
                case "missing":
                    summary.Missing++;
                    break;
                case "stale":
                    summary.Stale++;
                    break;
                case "empty":
                    summary.Empty++;
                    break;
                case "unreadable":
                    summary.Unreadable++;
                    break;
            }
        }

        return summary;
    }

    private static string MonitorKey(string savePath, string saveFile)
    {
        var path = string.IsNullOrWhiteSpace(savePath)
            ? string.Empty
            : FullPath(savePath);
        var file = string.IsNullOrWhiteSpace(saveFile)
            ? "amz-monitor"
            : saveFile.Trim();
        return $"{path}\0{file}";
    }

    private static string FullPath(string path) =>
        Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(path.Trim()));

    private static string KindName(AppPathKind kind) =>
        kind switch
        {
            AppPathKind.File => "file",
            AppPathKind.Directory => "directory",
            _ => "external",
        };

    private sealed record MonitorDescriptor(
        string Key,
        string SavePath,
        string SaveFile,
        string? Id,
        string Status,
        string? Mode);
}
