using AmazonScraper.Cli.Monitoring;
using AmazonScraper.Cli.Startup;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Web.Monitoring;

public interface IMonitorStartupInstaller
{
    string Kind { get; }

    bool IsAvailable(out string? error);

    MonitorStartupView Describe(MonitorPaths paths);

    Task<MonitorStartupView> InstallAsync(
        MonitorBinding binding,
        MonitorPaths paths,
        MonitorPlan plan,
        MonitorFormat format,
        MonitorLayout layout,
        int timerSeconds,
        CancellationToken cancellationToken = default);

    Task<MonitorStartupView> RemoveAsync(
        MonitorPaths paths,
        CancellationToken cancellationToken = default);

    Task<MonitorStartupView> StartAsync(
        MonitorPaths paths,
        CancellationToken cancellationToken = default);

    Task<MonitorStartupView> RestartAsync(
        MonitorPaths paths,
        CancellationToken cancellationToken = default);

    MonitorStartupView Verify(MonitorPaths paths);
}

/// <summary>
/// Web-side adapter over the same startup registry and OS backend used by
/// <c>amz-cli monitor --startup</c>. It never invents Web-only startup state.
/// </summary>
public sealed class MonitorStartupInstaller : IMonitorStartupInstaller
{
    private static readonly TimeSpan StartupWait = TimeSpan.FromSeconds(10);
    private readonly StartupManager? _manager;
    private readonly string? _creationError;
    private bool? _available;
    private string? _availabilityError;

    public MonitorStartupInstaller()
    {
        try
        {
            _manager = StartupManager.Create();
        }
        catch (Exception ex)
        {
            _manager = null;
            _creationError = ex.Message;
        }
    }

    public string Kind => _manager?.Kind ?? "none";

    public bool IsAvailable(out string? error)
    {
        if (_available is { } available)
        {
            error = _availabilityError;
            return available;
        }

        if (_manager is null)
        {
            _available = false;
            _availabilityError = _creationError ?? "Auto-start is unavailable.";
            error = _availabilityError;
            return false;
        }

        try
        {
            _manager.EnsureAvailable();
            _available = true;
            _availabilityError = null;
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            _available = false;
            _availabilityError = ex.Message;
            error = _availabilityError;
            return false;
        }
    }

    public MonitorStartupView Describe(MonitorPaths paths)
    {
        var (available, error) = Availability();
        var definition = Find(paths);
        var current = TryLoadPid(paths);
        var running = current is not null
            && MonitorProcessInspector.IsRunning(current);
        var managed = running
            && current is not null
            && (string.Equals(current.Mode, "startup", StringComparison.Ordinal)
                || (!string.IsNullOrWhiteSpace(definition?.Id)
                    && string.Equals(
                        current.StartupId,
                        definition.Id,
                        StringComparison.Ordinal)));
        var registered = false;
        if (definition is not null && _manager is not null && available)
        {
            try
            {
                registered = _manager.Exists(definition);
            }
            catch (Exception)
            {
                // A broken unit/task must not break the overview.
            }
        }

        return new MonitorStartupView
        {
            Available = available,
            Backend = available ? _manager?.Kind : null,
            Error = error,
            Configured = definition is not null,
            Enabled = definition?.Enabled ?? false,
            Registered = registered,
            Managed = managed,
            Id = definition?.Id,
            Name = definition?.Name,
            CommandPreview = definition?.CommandPreview,
        };
    }

    public async Task<MonitorStartupView> InstallAsync(
        MonitorBinding binding,
        MonitorPaths paths,
        MonitorPlan plan,
        MonitorFormat format,
        MonitorLayout layout,
        int timerSeconds,
        CancellationToken cancellationToken = default)
    {
        var manager = RequireManager();
        manager.EnsureAvailable();

        var commandLine = new List<string>
        {
            "monitor",
            "--binding",
            binding.Id,
            "--log-path",
            paths.LogPath,
        };
        var definition = StartupDefinitionFactory.Create(
            paths,
            plan,
            format,
            layout,
            timerSeconds,
            configPath: null,
            logPath: paths.LogPath,
            commandLine: commandLine);
        var current = TryLoadPid(paths);
        var running = current is not null
            && MonitorProcessInspector.IsRunning(current);
        var managed = running
            && current is not null
            && (string.Equals(current.Mode, "startup", StringComparison.Ordinal)
                || string.Equals(
                    current.StartupId,
                    definition.Id,
                    StringComparison.Ordinal));

        manager.Register(definition);

        if (running && !managed)
        {
            return WithMessage(
                Describe(paths),
                "Startup installed. The manually started monitor remains active; " +
                "startup will be used at the next login.");
        }

        if (running
            && managed
            && string.Equals(
                manager.Kind,
                WindowsTaskStartup.KindName,
                StringComparison.Ordinal))
        {
            await MonitorBackground.StopAsync(paths, quiet: true)
                .ConfigureAwait(false);
        }

        manager.Start(
            definition,
            restart: string.Equals(
                manager.Kind,
                SystemdUserStartup.KindName,
                StringComparison.Ordinal));

        var started = await WaitForStartupAsync(paths, cancellationToken)
            .ConfigureAwait(false);
        if (started is null)
        {
            throw new MonitorException(
                "Startup installed, but the monitor is not running. " +
                "Check the unit/task and the monitor log.");
        }

        return WithMessage(
            Describe(paths),
            $"Startup installed ({manager.Kind}). Monitor started (PID {started.Pid}).");
    }

    public async Task<MonitorStartupView> RemoveAsync(
        MonitorPaths paths,
        CancellationToken cancellationToken = default)
    {
        var manager = RequireManager();
        var definition = Find(paths);
        if (definition is null)
        {
            return WithMessage(Describe(paths), "No auto-start configured.");
        }

        var current = TryLoadPid(paths);
        var running = current is not null
            && MonitorProcessInspector.IsRunning(current);
        var managed = running
            && current is not null
            && (string.Equals(current.Mode, "startup", StringComparison.Ordinal)
                || string.Equals(
                    current.StartupId,
                    definition.Id,
                    StringComparison.Ordinal));

        var removed = manager.Remove(definition);
        if (managed && current is not null)
        {
            await MonitorBackground.StopAsync(paths, quiet: true)
                .ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return WithMessage(
            Describe(paths),
            removed
                ? $"Auto-start removed ({manager.Kind})."
                : "Auto-start definition removed.");
    }

    public async Task<MonitorStartupView> StartAsync(
        MonitorPaths paths,
        CancellationToken cancellationToken = default)
    {
        var manager = RequireManager();
        var definition = Find(paths)
            ?? throw new MonitorException("Auto-start is not configured.");
        var current = TryLoadPid(paths);
        if (current is not null && MonitorProcessInspector.IsRunning(current))
        {
            return Describe(paths);
        }

        manager.Start(definition);
        var started = await WaitForStartupAsync(paths, cancellationToken)
            .ConfigureAwait(false);
        if (started is null)
        {
            throw new MonitorException(
                "Startup started, but the monitor is not running.");
        }

        return WithMessage(
            Describe(paths),
            $"Monitor started from startup (PID {started.Pid}).");
    }

    public async Task<MonitorStartupView> RestartAsync(
        MonitorPaths paths,
        CancellationToken cancellationToken = default)
    {
        var manager = RequireManager();
        var definition = Find(paths)
            ?? throw new MonitorException("Auto-start is not configured.");
        manager.Start(definition, restart: true);
        var started = await WaitForStartupAsync(paths, cancellationToken)
            .ConfigureAwait(false);
        if (started is null)
        {
            throw new MonitorException(
                "Startup restarted, but the monitor is not running.");
        }

        return WithMessage(
            Describe(paths),
            $"Monitor restarted from startup (PID {started.Pid}).");
    }

    public MonitorStartupView Verify(MonitorPaths paths) => Describe(paths);

    private StartupDefinition? Find(MonitorPaths paths)
    {
        if (_manager is null)
        {
            return null;
        }

        try
        {
            return _manager.Registry.FindBySchedule(
                paths.SavePath,
                paths.BaseName);
        }
        catch (MonitorException)
        {
            return null;
        }
    }

    private StartupManager RequireManager() =>
        _manager ?? throw new MonitorException(
            _creationError ?? "Auto-start is unavailable.");

    private (bool Available, string? Error) Availability() =>
        IsAvailable(out var error)
            ? (true, null)
            : (false, error);

    private static MonitorPidInfo? TryLoadPid(MonitorPaths paths)
    {
        try
        {
            return MonitorPidInfo.TryLoad(paths.PidPath);
        }
        catch (MonitorException)
        {
            return null;
        }
    }

    private static async Task<MonitorPidInfo?> WaitForStartupAsync(
        MonitorPaths paths,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + StartupWait;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var current = TryLoadPid(paths);
            if (current is not null && MonitorProcessInspector.IsRunning(current))
            {
                return current;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private static MonitorStartupView WithMessage(
        MonitorStartupView view,
        string message)
    {
        view.Message = message;
        return view;
    }
}
