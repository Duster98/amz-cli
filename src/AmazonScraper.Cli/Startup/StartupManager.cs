using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Startup;

/// <summary>
/// Coordinates the per-user startup definition and the OS-specific registration.
/// </summary>
public sealed class StartupManager
{
    private readonly IStartupBackend _backend;

    public StartupManager(
        IStartupBackend backend,
        StartupRegistry? registry = null)
    {
        _backend = backend;
        Registry = registry ?? new StartupRegistry();
    }

    public StartupRegistry Registry { get; }

    public string Kind => _backend.Kind;

    public static StartupManager Create(
        IStartupProcessRunner? runner = null,
        string? startupDirectory = null,
        string? systemdUnitDirectory = null,
        string? windowsHelperPath = null)
    {
        var registry = new StartupRegistry(startupDirectory);
        if (OperatingSystem.IsWindows())
        {
            return new StartupManager(
                new WindowsTaskStartup(runner, windowsHelperPath),
                registry);
        }

        if (OperatingSystem.IsLinux())
        {
            return new StartupManager(
                new SystemdUserStartup(runner, systemdUnitDirectory),
                registry);
        }

        throw new MonitorException(
            "Auto-start is only supported on Linux (systemd) and Windows.");
    }

    public string NameFor(string id) => _backend.NameFor(id);

    public void EnsureAvailable()
    {
        if (!_backend.IsAvailable(out var error))
        {
            throw new MonitorException(
                error ?? "Auto-start unavailable.");
        }
    }

    public StartupDefinition Register(StartupDefinition definition)
    {
        EnsureAvailable();
        Prepare(definition);
        Registry.Save(definition);
        try
        {
            _backend.Install(definition);
        }
        catch (Exception)
        {
            try
            {
                _backend.Remove(definition);
            }
            catch (Exception)
            {
                // Best effort rollback.
            }

            Registry.Delete(definition.Id);
            throw;
        }

        return definition;
    }

    public void Start(StartupDefinition definition, bool restart = false)
    {
        if (restart)
        {
            _backend.Restart(definition);
            return;
        }

        _backend.Start(definition);
    }

    public void Stop(StartupDefinition definition)
    {
        _backend.Stop(definition);
    }

    public StartupDefinition Install(StartupDefinition definition)
    {
        Register(definition);
        try
        {
            _backend.Start(definition);
        }
        catch (Exception)
        {
            try
            {
                _backend.Remove(definition);
            }
            catch (Exception)
            {
                // Best effort rollback.
            }

            Registry.Delete(definition.Id);
            throw;
        }

        return definition;
    }

    public bool Remove(StartupDefinition definition)
    {
        Prepare(definition);
        var removed = _backend.Remove(definition);
        Registry.Delete(definition.Id);
        return removed;
    }

    public bool Exists(StartupDefinition definition)
    {
        Prepare(definition);
        return _backend.Exists(definition);
    }

    private void Prepare(StartupDefinition definition)
    {
        definition.Kind = _backend.Kind;
        definition.Name = _backend.NameFor(definition.Id);
        definition.Enabled = true;
    }
}
