using AmazonScraper.Cli.Monitoring;
using AmazonScraper.Cli.Web.Monitoring;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Tests;

internal sealed class FakeMonitorStartupInstaller : IMonitorStartupInstaller
{
    public bool Available { get; set; } = true;

    public string? AvailabilityError { get; set; }

    public string? LastMessage { get; private set; }

    public bool Configured { get; private set; }

    public int InstallCalls { get; private set; }

    public int RemoveCalls { get; private set; }

    public int StartCalls { get; private set; }

    public int RestartCalls { get; private set; }

    public int VerifyCalls { get; private set; }

    public string Kind => "fake-startup";

    public bool IsAvailable(out string? error)
    {
        error = AvailabilityError;
        return Available;
    }

    public MonitorStartupView Describe(MonitorPaths paths) =>
        new()
        {
            Available = Available,
            Backend = Available ? Kind : null,
            Error = AvailabilityError,
            Configured = Configured,
            Enabled = Configured,
            Registered = Configured,
            Managed = Configured,
            Id = "startup-test",
            Name = "amz-monitor-test.service",
            CommandPreview = "amz-cli monitor --binding test",
            Message = LastMessage,
        };

    public Task<MonitorStartupView> InstallAsync(
        MonitorBinding binding,
        MonitorPaths paths,
        MonitorPlan plan,
        MonitorFormat format,
        MonitorLayout layout,
        int timerSeconds,
        CancellationToken cancellationToken = default)
    {
        InstallCalls++;
        Configured = true;
        LastMessage = "Startup installed.";
        return Task.FromResult(Describe(paths));
    }

    public Task<MonitorStartupView> RemoveAsync(
        MonitorPaths paths,
        CancellationToken cancellationToken = default)
    {
        RemoveCalls++;
        Configured = false;
        LastMessage = "Auto-start removed.";
        return Task.FromResult(Describe(paths));
    }

    public Task<MonitorStartupView> StartAsync(
        MonitorPaths paths,
        CancellationToken cancellationToken = default)
    {
        StartCalls++;
        LastMessage = "Monitor started from startup.";
        return Task.FromResult(Describe(paths));
    }

    public Task<MonitorStartupView> RestartAsync(
        MonitorPaths paths,
        CancellationToken cancellationToken = default)
    {
        RestartCalls++;
        LastMessage = "Monitor restarted from startup.";
        return Task.FromResult(Describe(paths));
    }

    public MonitorStartupView Verify(MonitorPaths paths)
    {
        VerifyCalls++;
        return Describe(paths);
    }
}
