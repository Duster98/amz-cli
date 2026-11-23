using AmazonScraper.Cli.Startup;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Tests;

public class StartupDefinitionFactoryTests
{
    [Fact]
    public void BuildArguments_strips_control_flags_and_keeps_a_log_path()
    {
        var result = StartupDefinitionFactory.BuildArguments(
            [
                "/usr/bin/amz-cli",
                "monitor",
                "--config",
                "monitor.json",
                "--startup",
                "--detach",
                "--list",
                "--log-path",
                "/tmp/monitor.log",
            ],
            "/tmp/default.log");

        Assert.Equal(
            [
                "monitor",
                "--config",
                "monitor.json",
                "--log-path",
                "/tmp/monitor.log",
            ],
            result);
    }

    [Fact]
    public void BuildArguments_adds_the_default_log_path_when_missing()
    {
        var result = StartupDefinitionFactory.BuildArguments(
            ["amz-cli", "monitor", "--config", "monitor.json", "--startup"],
            "/tmp/default.log");

        Assert.Equal(
            [
                "monitor",
                "--config",
                "monitor.json",
                "--log-path",
                "/tmp/default.log",
            ],
            result);
    }

    [Fact]
    public void BuildArguments_drops_control_flags_with_values()
    {
        var result = StartupDefinitionFactory.BuildArguments(
            [
                "amz-cli",
                "monitor",
                "--config",
                "monitor.json",
                "--cycles",
                "2",
                "--html",
                "page.html",
                "--startup",
            ],
            "/tmp/default.log");

        Assert.Equal(
            [
                "monitor",
                "--config",
                "monitor.json",
                "--log-path",
                "/tmp/default.log",
            ],
            result);
    }

    [Fact]
    public void BuildArguments_rejects_a_command_without_monitor()
    {
        Assert.Throws<MonitorException>(() =>
            StartupDefinitionFactory.BuildArguments(
                ["amz-cli", "search", "test"],
                "/tmp/default.log"));
    }

    [Fact]
    public void RedactCommand_hides_cookie_values()
    {
        var preview = StartupDefinitionFactory.RedactCommand(
            [
                "monitor",
                "--cookie",
                "secret-cookie",
                "--domain-cookie=session=abc",
                "--timer",
                "60",
            ]);

        Assert.Equal(
            "monitor --cookie *** --domain-cookie=*** --timer 60",
            preview);
        Assert.DoesNotContain("secret-cookie", preview);
        Assert.DoesNotContain("session=abc", preview);
    }
}
