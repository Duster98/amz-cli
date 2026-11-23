using AmazonScraper.Cli.Startup;

namespace AmazonScraper.Cli.Tests;

public class SystemdUserStartupTests
{
    [Fact]
    public void BuildUnit_contains_exec_start_and_environment()
    {
        var definition = Definition();

        var unit = SystemdUserStartup.BuildUnit(definition);

        Assert.Contains(
            "ExecStart=/usr/bin/amz-cli monitor --startup-run abc123def456",
            unit);
        Assert.Contains("Type=simple", unit);
        Assert.Contains("WorkingDirectory=\"/home/user/My Project\"", unit);
        Assert.Contains(
            "Environment=AMZ_MONITOR_STARTUP=systemd-user",
            unit);
        Assert.Contains(
            "Environment=AMZ_MONITOR_STARTUP_ID=abc123def456",
            unit);
        Assert.Contains("WantedBy=default.target", unit);
    }

    [Fact]
    public void BuildUnit_includes_prefix_arguments()
    {
        var definition = Definition();
        definition.Executable = "/usr/bin/dotnet";
        definition.PrefixArguments = ["/opt/amz-cli/amazon.dll"];

        var unit = SystemdUserStartup.BuildUnit(definition);

        Assert.Contains(
            "ExecStart=/usr/bin/dotnet /opt/amz-cli/amazon.dll monitor " +
            "--startup-run abc123def456",
            unit);
    }

    [Fact]
    public void Quote_handles_spaces_and_specifiers()
    {
        Assert.Equal("/tmp/plain", SystemdUserStartup.Quote("/tmp/plain"));
        Assert.Equal(
            "\"/tmp/a b\"",
            SystemdUserStartup.Quote("/tmp/a b"));
        Assert.Equal(
            "\"/tmp/100%%\"",
            SystemdUserStartup.Quote("/tmp/100%"));
    }

    [Fact]
    public void Install_writes_the_unit_and_enables_it()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = new TempDirectory();
        var runner = new RecordingRunner();
        var backend = new SystemdUserStartup(runner, directory.Root);
        var definition = Definition();

        backend.Install(definition);

        var unitPath = Path.Combine(directory.Root, definition.Name);
        Assert.True(File.Exists(unitPath));
        Assert.Contains("ExecStart=", File.ReadAllText(unitPath));
        Assert.Equal(
            [
                "--user daemon-reload",
                "--user enable amz-monitor-abc123def456.service",
            ],
            runner.Calls.Select(call => string.Join(' ', call.Arguments)));
    }

    [Fact]
    public void Start_Restart_and_Remove_use_systemctl()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = new TempDirectory();
        var runner = new RecordingRunner();
        var backend = new SystemdUserStartup(runner, directory.Root);
        var definition = Definition();
        backend.Install(definition);
        runner.Calls.Clear();

        backend.Start(definition);
        backend.Restart(definition);
        backend.Stop(definition);
        var removed = backend.Remove(definition);

        Assert.True(removed);
        Assert.False(File.Exists(Path.Combine(directory.Root, definition.Name)));
        Assert.Equal(
            [
                "--user start amz-monitor-abc123def456.service",
                "--user restart amz-monitor-abc123def456.service",
                "--user stop amz-monitor-abc123def456.service",
                "--user disable --now amz-monitor-abc123def456.service",
                "--user daemon-reload",
                "--user reset-failed amz-monitor-abc123def456.service",
            ],
            runner.Calls.Select(call => string.Join(' ', call.Arguments)));
    }

    [Fact]
    public void Exists_reports_the_unit_file()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = new TempDirectory();
        var backend = new SystemdUserStartup(new RecordingRunner(), directory.Root);
        var definition = Definition();
        backend.Install(definition);

        Assert.True(backend.Exists(definition));
        backend.Remove(definition);
        Assert.False(backend.Exists(definition));
    }

    private static StartupDefinition Definition() => new()
    {
        Id = "abc123def456",
        Name = "amz-monitor-abc123def456.service",
        Kind = "systemd-user",
        Enabled = true,
        Executable = "/usr/bin/amz-cli",
        WorkingDirectory = "/home/user/My Project",
        SavePath = "/home/user/My Project/monitor",
        SaveFile = "amz-monitor",
        Domains = ["it"],
        Targets = 1,
        TimerSeconds = 3600,
        Arguments = ["monitor", "--config", "/tmp/monitor.json"],
    };

    private sealed class RecordingRunner : IStartupProcessRunner
    {
        public List<(string FileName, string[] Arguments)> Calls { get; } = [];

        public StartupProcessResult Run(
            string fileName,
            IReadOnlyList<string> arguments,
            string? workingDirectory = null,
            TimeSpan? timeout = null)
        {
            Calls.Add((fileName, arguments.ToArray()));
            return new StartupProcessResult(0, string.Empty, string.Empty);
        }
    }
}
