using AmazonScraper.Cli.Web;
using AmazonScraper.Update;

namespace AmazonScraper.Cli.Tests;

public class WebResumeLauncherTests
{
    [Fact]
    public async Task Launches_the_updated_binary_with_the_resume_state()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = new TempDirectory();
        var install = Path.Combine(directory.Root, "install");
        Directory.CreateDirectory(install);
        var executable = Path.Combine(install, "amz-cli");
        var argsPath = Path.Combine(install, "args.txt");
        await File.WriteAllTextAsync(
            executable,
            $"#!/bin/sh\nprintf '%s\\n' \"$@\" > \"{argsPath}\"\n");
        File.SetUnixFileMode(
            executable,
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute);
        var resumePath = Path.Combine(directory.Root, "resume.json");
        await File.WriteAllTextAsync(resumePath, "{}");

        var result = WebResumeLauncher.TryStart(new UpdateApplyPlan
        {
            RestartWeb = true,
            WebResumePath = resumePath,
            InstallDir = install,
            Platform = "linux-x64",
        });

        Assert.Null(result.Error);
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (File.Exists(argsPath))
            {
                break;
            }
            await Task.Delay(50);
        }
        var args = await File.ReadAllLinesAsync(argsPath);
        Assert.Equal(["__web-resume", "--state", resumePath], args);
    }
}
