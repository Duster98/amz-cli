using System.Text.Json;
using AmazonScraper.Cli;
using AmazonScraper.Cli.Diagnostics;
using AmazonScraper.Update;

namespace AmazonScraper.Cli.Tests;

public class DoctorServiceTests
{
    [Fact]
    public async Task Offline_report_checks_the_local_installation()
    {
        using var install = new TempDirectory();
        using var state = new TempDirectory();
        using var skill = new TempDirectory();
        var executable = Path.Combine(install.Root, "amz-cli");
        File.WriteAllText(executable, "binary");
        var manifest = new
        {
            schema = 1,
            product = "amz-cli",
            version = VersionInfo.Informational,
            platform = PlatformInfo.Detect(),
            install_dir = install.Root,
            files = new[] { "amz-cli" },
        };
        File.WriteAllText(
            Path.Combine(install.Root, "install.json"),
            JsonSerializer.Serialize(manifest));

        var report = await DoctorService.RunAsync(
            new DoctorOptions
            {
                Offline = true,
                InstallDir = install.Root,
                StateRoot = state.Root,
                SkillDirectory = skill.Root,
            },
            CancellationToken.None);

        Assert.Equal(VersionInfo.Informational, report.Version);
        Assert.Contains(report.Checks, check => check.Id == "runtime");
        Assert.Contains(report.Checks, check => check.Id == "install");
        Assert.Contains(report.Checks, check => check.Id == "monitors");
        Assert.Contains(report.Checks, check => check.Id == "startup");
        Assert.Contains(report.Checks, check => check.Id == "skill");
        Assert.Contains(report.Checks, check => check.Id == "web");
        Assert.Contains(report.Checks, check => check.Id == "update");
        Assert.False(report.HasErrors);
        Assert.Equal(0, report.ErrorCount);
        var installCheck = Assert.Single(
            report.Checks,
            check => check.Id == "install");
        Assert.Equal(DoctorStatus.Ok, installCheck.Status);
    }

    [Fact]
    public async Task Older_install_record_is_not_an_error_after_a_cli_update()
    {
        using var install = new TempDirectory();
        using var state = new TempDirectory();
        using var skill = new TempDirectory();
        File.WriteAllText(Path.Combine(install.Root, "amz-cli"), "binary");
        File.WriteAllText(
            Path.Combine(install.Root, "install.json"),
            JsonSerializer.Serialize(new
            {
                schema = 1,
                product = "amz-cli",
                version = "0.9.0",
                platform = PlatformInfo.Detect(),
                install_dir = install.Root,
                files = new[] { "amz-cli" },
            }));

        var report = await DoctorService.RunAsync(
            new DoctorOptions
            {
                Offline = true,
                InstallDir = install.Root,
                StateRoot = state.Root,
                SkillDirectory = skill.Root,
            },
            CancellationToken.None);

        var installCheck = Assert.Single(
            report.Checks,
            check => check.Id == "install");
        Assert.Equal(DoctorStatus.Ok, installCheck.Status);
        Assert.Contains("binary updated", installCheck.Detail);
    }
}
