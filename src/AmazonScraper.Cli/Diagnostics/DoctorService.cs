using System.Security.Cryptography;
using System.Text.Json;
using AmazonScraper.Cli.EnvSetup;
using AmazonScraper.Cli.Monitoring;
using AmazonScraper.Cli.Skill;
using AmazonScraper.Cli.Startup;
using AmazonScraper.Cli.Storage;
using AmazonScraper.Cli.Web;
using AmazonScraper.Core.Diagnostics;
using AmazonScraper.Update;

namespace AmazonScraper.Cli.Diagnostics;

public sealed class DoctorOptions
{
    public bool Offline { get; init; }

    public string Channel { get; init; } = "stable";

    public string? InstallDir { get; init; }

    public string? StateRoot { get; init; }

    public string? SkillDirectory { get; init; }
}

/// <summary>
/// Read-only health check for the local installation, update channel, monitor
/// registry, startup registration, Windows notifier subsystem and agent skill.
/// It never applies updates or deletes monitor entries.
/// </summary>
public static class DoctorService
{
    public static async Task<DoctorReport> RunAsync(
        DoctorOptions options,
        CancellationToken cancellationToken)
    {
        var installDir = ResolveInstallDir(options.InstallDir);
        var stateRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(
                options.StateRoot ?? UserStatePaths.Root()));
        var platform = PlatformInfo.Detect();

        var checks = new List<DoctorCheck>
        {
            CheckRuntime(),
            CheckInstall(installDir, platform),
            CheckNotifier(installDir),
            CheckPath(installDir),
            await CheckWebAsync(stateRoot, cancellationToken).ConfigureAwait(false),
            await CheckUpdateAsync(options, platform, cancellationToken)
                .ConfigureAwait(false),
            CheckMonitors(stateRoot),
            CheckStartups(stateRoot),
            CheckSkill(options.SkillDirectory),
            CheckState(stateRoot),
        };

        return new DoctorReport(
            VersionInfo.Informational,
            platform,
            installDir,
            stateRoot,
            checks);
    }

    private static DoctorCheck CheckRuntime()
    {
        var result = RuntimeDiagnostics.Run(VersionInfo.Informational);
        var detail =
            $"{result.OperatingSystem}; {result.RuntimeIdentifier}; " +
            $".NET {result.DotNetVersion}; {result.Transport}; {result.CurlVersion}";
        if (!string.IsNullOrWhiteSpace(result.CurlError))
        {
            detail += $"; curl: {result.CurlError}";
        }

        return new DoctorCheck(
            "runtime",
            "Runtime and transport",
            result.CurlAvailable ? DoctorStatus.Ok : DoctorStatus.Warning,
            detail);
    }

    private static async Task<DoctorCheck> CheckWebAsync(
        string stateRoot,
        CancellationToken cancellationToken)
    {
        var assets = WebAssetStore.Load(webDirectory: null);
        if (assets.IsFallback)
        {
            return new DoctorCheck(
                "web",
                "Web interface",
                DoctorStatus.Warning,
                "Web bundle missing: run './web/build.sh' and republish the CLI.");
        }

        var session = WebSessionStore.TryRead(
            Path.Combine(stateRoot, "web", "session.json"));
        if (session is null)
        {
            return new DoctorCheck(
                "web",
                "Web interface",
                DoctorStatus.Ok,
                $"Assets ready (build {assets.BuildHash}); interface not running.");
        }

        var running = await WebControl
            .PingAsync(session, cancellationToken)
            .ConfigureAwait(false);
        if (running)
        {
            return new DoctorCheck(
                "web",
                "Web interface",
                DoctorStatus.Ok,
                $"Running at http://127.0.0.1:{session.Port}/ (PID {session.Pid}).");
        }

        return new DoctorCheck(
            "web",
            "Web interface",
            DoctorStatus.Info,
            "Web session not running: it will be replaced on next start.");
    }

    private static DoctorCheck CheckInstall(string installDir, string platform)
    {
        var manifestPath = Path.Combine(installDir, "install.json");
        if (!File.Exists(manifestPath))
        {
            return new DoctorCheck(
                "install",
                "Installation",
                DoctorStatus.Warning,
                $"install.json missing in {installDir} " +
                "(source build or incomplete installation).");
        }

        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(manifestPath));
            var root = document.RootElement;
            var version = GetString(root, "version");
            var manifestPlatform = GetString(root, "platform");
            var product = GetString(root, "product");
            var files = new List<string>();
            if (root.TryGetProperty("files", out var filesElement)
                && filesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in filesElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String
                        && item.GetString() is { Length: > 0 } file)
                    {
                        files.Add(file);
                    }
                }
            }

            var issues = new List<string>();
            var versionNote = string.Empty;
            if (!string.Equals(
                    version,
                    VersionInfo.Informational,
                    StringComparison.Ordinal))
            {
                // install.json is written by the standalone installer; a CLI
                // self-update updates the binary but not this record. A newer
                // binary is normal, an older one is a downgrade.
                if (SemanticVersion.TryParse(
                        version,
                        out var installedVersion)
                    && SemanticVersion.TryParse(
                        VersionInfo.Informational,
                        out var binaryVersion)
                    && binaryVersion.CompareTo(installedVersion) >= 0)
                {
                    versionNote =
                        $" (install.json {version}; binary updated to " +
                        $"{VersionInfo.Informational})";
                }
                else
                {
                    issues.Add(
                        $"install.json version {version ?? "?"} != binary " +
                        VersionInfo.Informational);
                }
            }
            if (!string.Equals(
                    manifestPlatform,
                    platform,
                    StringComparison.Ordinal))
            {
                issues.Add(
                    $"install.json platform {manifestPlatform ?? "?"} != " +
                    platform);
            }
            if (!string.Equals(
                    product,
                    "amz-cli",
                    StringComparison.Ordinal))
            {
                issues.Add($"unexpected product: {product ?? "?"}");
            }

            var missing = new List<string>();
            foreach (var file in files)
            {
                try
                {
                    var full = PathSafety.CombineSafe(installDir, file);
                    if (!File.Exists(full))
                    {
                        missing.Add(file);
                    }
                }
                catch (Exception)
                {
                    missing.Add(file);
                }
            }
            if (missing.Count > 0)
            {
                issues.Add("missing files: " + string.Join(", ", missing.Take(5)));
            }

            var detail =
                $"version {version ?? "?"}; platform {manifestPlatform ?? "?"}; " +
                $"{files.Count} files{versionNote}";
            if (issues.Count > 0)
            {
                detail += "; " + string.Join("; ", issues);
            }

            return new DoctorCheck(
                "install",
                "Installation",
                issues.Count == 0 ? DoctorStatus.Ok : DoctorStatus.Warning,
                detail);
        }
        catch (Exception ex)
        {
            return new DoctorCheck(
                "install",
                "Installation",
                DoctorStatus.Warning,
                $"install.json unreadable: {ex.Message}");
        }
    }

    private static DoctorCheck CheckNotifier(string installDir)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new DoctorCheck(
                "notifier",
                "Notification helper",
                DoctorStatus.Info,
                "Not applicable on this platform.");
        }

        var candidates = new[]
        {
            Path.Combine(installDir, "amz-notify.exe"),
            Path.Combine(installDir, "notify", "amz-notify.exe"),
        };
        var path = candidates.FirstOrDefault(File.Exists);
        if (path is null)
        {
            return new DoctorCheck(
                "notifier",
                "Notification helper",
                DoctorStatus.Warning,
                "amz-notify.exe is missing; desktop notifications will not work.");
        }

        if (!PeSubsystem.TryRead(path, out var subsystem))
        {
            return new DoctorCheck(
                "notifier",
                "Notification helper",
                DoctorStatus.Warning,
                $"PE subsystem unreadable: {path}");
        }

        return subsystem == 2
            ? new DoctorCheck(
                "notifier",
                "Notification helper",
                DoctorStatus.Ok,
                $"GUI (subsystem 2): {path}")
            : new DoctorCheck(
                "notifier",
                "Notification helper",
                DoctorStatus.Warning,
                $"Subsystem {subsystem} (console) in {path}; " +
                "reinstall version 2.30.0 or later.");
    }

    private static DoctorCheck CheckPath(string installDir)
    {
        if (IsDotnetHost())
        {
            return new DoctorCheck(
                "path",
                "PATH",
                DoctorStatus.Info,
                "Running from source (dotnet host): PATH and symlink not verified.");
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                var inPath = WindowsPathInstaller.IsExecutableDirectoryInPath();
                return inPath
                    ? new DoctorCheck(
                        "path",
                        "PATH",
                        DoctorStatus.Ok,
                        $"The directory is in PATH: {installDir}")
                    : new DoctorCheck(
                        "path",
                        "PATH",
                        DoctorStatus.Warning,
                        $"The directory is not in PATH: {installDir}. " +
                        "Run 'amz-cli --install-env-var' and reopen the terminal.");
            }
            catch (Exception ex)
            {
                return new DoctorCheck(
                    "path",
                    "PATH",
                    DoctorStatus.Warning,
                    $"PATH check unavailable: {ex.Message}");
            }
        }

        if (!OperatingSystem.IsLinux())
        {
            return new DoctorCheck(
                "path",
                "PATH",
                DoctorStatus.Info,
                "PATH check unavailable on this platform.");
        }

        var home = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        var bin = Path.Combine(home, ".local", "bin");
        var link = Path.Combine(bin, "amz-cli");
        var executable = Path.Combine(installDir, "amz-cli");
        var linkOk = false;
        try
        {
            var target = File.ResolveLinkTarget(link, returnFinalTarget: true);
            linkOk = target is not null
                && string.Equals(
                    Path.GetFullPath(target.FullName),
                    Path.GetFullPath(executable),
                    StringComparison.Ordinal);
        }
        catch (Exception)
        {
            // Missing or non-symlink path.
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathOk = pathValue
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(entry =>
            {
                try
                {
                    return string.Equals(
                        Path.TrimEndingDirectorySeparator(
                            Path.GetFullPath(entry.Trim())),
                        Path.TrimEndingDirectorySeparator(
                            Path.GetFullPath(bin)),
                        StringComparison.Ordinal);
                }
                catch (Exception)
                {
                    return false;
                }
            });

        if (linkOk && pathOk)
        {
            return new DoctorCheck(
                "path",
                "PATH",
                DoctorStatus.Ok,
                $"Symlink and PATH are correct: {link}");
        }

        var detail = linkOk
            ? $"Symlink is correct ({link}), but {bin} is not in PATH."
            : $"Symlink missing or incorrect: {link}. " +
              $"Create it with 'ln -sf \"{executable}\" \"{link}\"'.";
        if (!pathOk)
        {
            detail += $" Add {bin} to PATH.";
        }

        return new DoctorCheck(
            "path",
            "PATH",
            DoctorStatus.Warning,
            detail);
    }

    private static async Task<DoctorCheck> CheckUpdateAsync(
        DoctorOptions options,
        string platform,
        CancellationToken cancellationToken)
    {
        if (options.Offline)
        {
            return new DoctorCheck(
                "update",
                "Updates",
                DoctorStatus.Info,
                "Online check skipped (--offline).");
        }

        try
        {
            using var api = new UpdateApiClient(
                currentVersion: VersionInfo.Informational);
            var latest = await api.GetLatestAsync(
                    platform,
                    options.Channel,
                    cancellationToken)
                .ConfigureAwait(false);
            if (latest is null)
            {
                return new DoctorCheck(
                    "update",
                    "Updates",
                    DoctorStatus.Warning,
                    $"No {options.Channel} release for {platform}.");
            }

            ReleaseSignatureVerifier.VerifyOrThrow(
                latest.Manifest,
                latest.Signature,
                latest.SigningKeyId);

            if (!SemanticVersion.TryParse(
                    VersionInfo.Informational,
                    out var currentVersion)
                || !SemanticVersion.TryParse(
                    latest.Version,
                    out var latestVersion))
            {
                return new DoctorCheck(
                    "update",
                    "Updates",
                    DoctorStatus.Ok,
                    $"Release {latest.Version} ({options.Channel}); signature valid.");
            }

            var comparison = currentVersion.CompareTo(latestVersion);
            if (comparison < 0)
            {
                return new DoctorCheck(
                    "update",
                    "Updates",
                    DoctorStatus.Info,
                    $"Update available: {latest.Version} " +
                    $"({options.Channel}); signature valid.");
            }
            if (comparison > 0)
            {
                return new DoctorCheck(
                    "update",
                    "Updates",
                    DoctorStatus.Warning,
                    $"Local version {VersionInfo.Informational} is newer " +
                    $"than release {latest.Version} ({options.Channel}).");
            }

            return new DoctorCheck(
                "update",
                "Updates",
                DoctorStatus.Ok,
                $"Up to date with {latest.Version} ({options.Channel}); signature valid.");
        }
        catch (Exception ex) when (
            ex is UpdateException or CryptographicException)
        {
            return new DoctorCheck(
                "update",
                "Updates",
                DoctorStatus.Error,
                $"Invalid release signature or manifest: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new DoctorCheck(
                "update",
                "Updates",
                DoctorStatus.Warning,
                $"Online check unavailable: {ex.Message}");
        }
    }

    private static DoctorCheck CheckMonitors(string stateRoot)
    {
        try
        {
            var registry = new MonitorRegistry(
                Path.Combine(stateRoot, "monitors"));
            var startups = new StartupRegistry(
                Path.Combine(stateRoot, "startups"));
            var discovery = new MonitorDiscovery(
                registry,
                [Environment.CurrentDirectory],
                startups);
            var result = discovery.Discover(
                includeStale: true,
                prune: false);
            var registryEntries = registry.ReadAll();
            var registryStale = registryEntries.Count(
                entry => !MonitorProcessInspector.IsRunning(entry));
            var running = result.Entries.Count(
                entry => entry.Status == "running");
            var stale = result.Entries.Count(
                entry => entry.Status == "stale");
            var configured = result.Entries.Count(
                entry => entry.Status == "configured");
            var detail =
                $"{running} running, {stale} stale, {configured} configured, " +
                $"{registryStale} stale registry files";

            if (stale > 0 || registryStale > 0)
            {
                return new DoctorCheck(
                    "monitors",
                    "Monitor",
                    DoctorStatus.Warning,
                    detail + "; use 'amz-cli monitor --list --prune'.");
            }
            if (running > 0)
            {
                return new DoctorCheck(
                    "monitors",
                    "Monitor",
                    DoctorStatus.Ok,
                    detail + ".");
            }
            return new DoctorCheck(
                "monitors",
                "Monitor",
                DoctorStatus.Info,
                configured > 0
                    ? detail + "; startup will run at the next login."
                    : "No monitors registered.");
        }
        catch (Exception ex)
        {
            return new DoctorCheck(
                "monitors",
                "Monitor",
                DoctorStatus.Warning,
                $"Monitor registry unreadable: {ex.Message}");
        }
    }

    private static DoctorCheck CheckStartups(string stateRoot)
    {
        try
        {
            var startupDirectory = Path.Combine(stateRoot, "startups");
            var startups = new StartupRegistry(startupDirectory).ReadAll();
            if (startups.Count == 0)
            {
                return new DoctorCheck(
                    "startup",
                    "Auto-start",
                    DoctorStatus.Info,
                    "No startup configured.");
            }

            StartupManager manager;
            try
            {
                manager = StartupManager.Create(
                    startupDirectory: startupDirectory);
            }
            catch (Exception ex)
            {
                return new DoctorCheck(
                    "startup",
                    "Auto-start",
                    DoctorStatus.Warning,
                    $"Startup backend unavailable: {ex.Message}");
            }

            var missing = new List<string>();
            foreach (var definition in startups)
            {
                try
                {
                    if (!manager.Exists(definition))
                    {
                        missing.Add(definition.Name ?? definition.Id);
                    }
                }
                catch (Exception ex)
                {
                    missing.Add(
                        $"{definition.Name ?? definition.Id} ({ex.Message})");
                }
            }

            if (missing.Count == 0)
            {
                return new DoctorCheck(
                    "startup",
                    "Auto-start",
                    DoctorStatus.Ok,
                    $"{startups.Count} startup entries registered and present.");
            }

            return new DoctorCheck(
                "startup",
                "Auto-start",
                DoctorStatus.Warning,
                $"{missing.Count}/{startups.Count} startup entries missing: " +
                $"{string.Join(", ", missing.Take(3))}. " +
                "Reinstall with 'amz-cli monitor --startup'.");
        }
        catch (Exception ex)
        {
            return new DoctorCheck(
                "startup",
                "Auto-start",
                DoctorStatus.Warning,
                $"Startup check unavailable: {ex.Message}");
        }
    }

    private static DoctorCheck CheckSkill(string? directory)
    {
        try
        {
            var result = SkillInstaller.Check(directory);
            return result.Status switch
            {
                SkillInstallStatus.UpToDate => new DoctorCheck(
                    "skill",
                    "Agent skill",
                    DoctorStatus.Ok,
                    result.Message),
                SkillInstallStatus.Missing => new DoctorCheck(
                    "skill",
                    "Agent skill",
                    DoctorStatus.Warning,
                    result.Message + " Run 'amz-cli skill --install'."),
                _ => new DoctorCheck(
                    "skill",
                    "Agent skill",
                    DoctorStatus.Warning,
                    result.Message),
            };
        }
        catch (Exception ex)
        {
            return new DoctorCheck(
                "skill",
                "Agent skill",
                DoctorStatus.Warning,
                $"Skill check unavailable: {ex.Message}");
        }
    }

    private static DoctorCheck CheckState(string stateRoot)
    {
        return Directory.Exists(stateRoot)
            ? new DoctorCheck(
                "state",
                "User state",
                DoctorStatus.Ok,
                stateRoot)
            : new DoctorCheck(
                "state",
                "User state",
                DoctorStatus.Info,
                $"It will be created on first use: {stateRoot}");
    }

    private static string ResolveInstallDir(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(requested));
        }

        if (IsDotnetHost())
        {
            return Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(AppContext.BaseDirectory));
        }

        return Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(InstallLocation.Resolve()));
    }

    private static bool IsDotnetHost()
    {
        var processPath = Environment.ProcessPath;
        return string.IsNullOrWhiteSpace(processPath)
            || string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                "dotnet",
                StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
}
