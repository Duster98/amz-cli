using AmazonScraper.Cli.Startup;

namespace AmazonScraper.Cli.Tests;

public class WindowsTaskStartupTests
{
    [Fact]
    public void BuildTaskXml_is_hidden_and_uses_the_helper_launcher()
    {
        var definition = Definition();

        var xml = WindowsTaskStartup.BuildTaskXml(
            definition,
            @"C:\Tools\amz-notify.exe",
            @"DOMAIN\user");

        Assert.Contains("<Hidden>true</Hidden>", xml);
        Assert.Contains("<LogonTrigger>", xml);
        Assert.Contains("<UserId>DOMAIN\\user</UserId>", xml);
        Assert.Contains(
            "<Command>C:\\Tools\\amz-notify.exe</Command>",
            xml);
        Assert.Contains(
            "<Arguments>--startup-launch abc123def456</Arguments>",
            xml);
        Assert.Contains(
            "<WorkingDirectory>C:\\My Project</WorkingDirectory>",
            xml);
        Assert.Contains("InteractiveToken", xml);
        Assert.Contains("LeastPrivilege", xml);
        Assert.Contains("PT0S", xml);
        Assert.DoesNotContain("--config", xml);
        Assert.DoesNotContain("--cookie", xml);
    }

    [Fact]
    public void BuildTaskXml_is_well_formed()
    {
        var xml = WindowsTaskStartup.BuildTaskXml(
            Definition(),
            @"C:\Tools\amz-notify.exe",
            @"DOMAIN\user");

        var document = System.Xml.Linq.XDocument.Parse(xml);

        Assert.Equal(
            "Task",
            document.Root?.Name.LocalName);
    }

    [Fact]
    public void BuildTaskXml_escapes_xml_characters()
    {
        var definition = Definition();
        definition.WorkingDirectory = @"C:\A&B";

        var xml = WindowsTaskStartup.BuildTaskXml(
            definition,
            @"C:\Tools\A&B.exe",
            "DOM<user>");

        Assert.Contains("A&amp;B.exe", xml);
        Assert.Contains("C:\\A&amp;B", xml);
        Assert.Contains("DOM&lt;user&gt;", xml);
    }

    [Fact]
    public void Install_Start_Stop_and_Remove_stop_the_monitor_first()
    {
        var runner = new RecordingRunner();
        var backend = new WindowsTaskStartup(runner);
        var definition = Definition();

        backend.Install(definition);
        backend.Start(definition);
        backend.Stop(definition);
        var removed = backend.Remove(definition);

        Assert.True(removed);
        Assert.Equal(
            [
                "/Create",
                "/Run",
                "monitor",
                "/End",
                "monitor",
                "/End",
                "/Delete",
            ],
            runner.Calls.Select(call => call.Arguments[0]));

        var monitorCalls = runner.Calls
            .Where(call => call.Arguments[0] == "monitor")
            .ToList();
        Assert.Equal(2, monitorCalls.Count);
        Assert.All(
            monitorCalls,
            call =>
            {
                Assert.EndsWith(
                    "amz-cli.exe",
                    call.FileName,
                    StringComparison.OrdinalIgnoreCase);
                Assert.Equal(
                    [
                        "monitor",
                        "--stop",
                        "--save-path",
                        @"C:\My Project\monitor",
                        "--save-file",
                        "amz-monitor",
                    ],
                    call.Arguments);
            });

        Assert.Contains(
            runner.Calls,
            call => call.Arguments[0] == "/Create"
                && call.Arguments[1] == "/TN"
                && call.Arguments[2] == definition.Name
                && call.Arguments[3] == "/XML"
                && call.Arguments[4].EndsWith(".xml", StringComparison.Ordinal)
                && call.Arguments[5] == "/F");
        Assert.Contains(
            runner.Calls,
            call => call.Arguments[0] == "/Delete"
                && call.Arguments[1] == "/TN"
                && call.Arguments[2] == definition.Name
                && call.Arguments[3] == "/F");
    }

    [Fact]
    public void Stop_falls_back_to_schtasks_when_the_monitor_stop_fails()
    {
        var runner = new FailingMonitorRunner();
        var backend = new WindowsTaskStartup(runner);
        var definition = Definition();

        backend.Stop(definition);

        Assert.Equal(
            ["monitor", "/End"],
            runner.Calls.Select(call => call.Arguments[0]));
    }

    [Fact]
    public void IsAvailable_is_false_off_windows()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var backend = new WindowsTaskStartup(new RecordingRunner());
        Assert.False(backend.IsAvailable(out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    private static StartupDefinition Definition() => new()
    {
        Id = "abc123def456",
        Name = "AmzCLI-abc123def456",
        Kind = "windows-task",
        Enabled = true,
        Executable = @"C:\Tools\amz-cli.exe",
        WorkingDirectory = @"C:\My Project",
        SavePath = @"C:\My Project\monitor",
        SaveFile = "amz-monitor",
        Domains = ["it"],
        Targets = 1,
        TimerSeconds = 3600,
        Arguments = ["monitor", "--config", @"C:\My Project\monitor.json"],
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

    private sealed class FailingMonitorRunner : IStartupProcessRunner
    {
        public List<(string FileName, string[] Arguments)> Calls { get; } = [];

        public StartupProcessResult Run(
            string fileName,
            IReadOnlyList<string> arguments,
            string? workingDirectory = null,
            TimeSpan? timeout = null)
        {
            Calls.Add((fileName, arguments.ToArray()));
            if (arguments.Count > 0
                && string.Equals(arguments[0], "monitor", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("monitor stop failed");
            }

            return new StartupProcessResult(0, string.Empty, string.Empty);
        }
    }
}
