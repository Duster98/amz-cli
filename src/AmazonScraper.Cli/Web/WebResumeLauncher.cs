using System.Diagnostics;
using AmazonScraper.Update;

namespace AmazonScraper.Cli.Web;

public sealed record WebResumeLaunch(int? ProcessId, string? Error);

public static class WebResumeLauncher
{
    public static WebResumeLaunch TryStart(UpdateApplyPlan plan)
    {
        if (!plan.RestartWeb)
        {
            return new WebResumeLaunch(null, null);
        }
        if (string.IsNullOrWhiteSpace(plan.WebResumePath)
            || !File.Exists(plan.WebResumePath))
        {
            return new WebResumeLaunch(
                null,
                "The Web resume state is missing.");
        }

        var executable = InstallLocation.ExecutablePath(
            plan.InstallDir,
            plan.Platform);
        if (!File.Exists(executable))
        {
            return new WebResumeLaunch(
                null,
                $"The updated executable was not found: {executable}");
        }

        try
        {
            var setsid = OperatingSystem.IsWindows() ? null : FindSetsid();
            var startInfo = new ProcessStartInfo
            {
                FileName = setsid ?? executable,
                WorkingDirectory = plan.InstallDir,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (setsid is not null)
            {
                startInfo.ArgumentList.Add(executable);
            }
            startInfo.ArgumentList.Add("__web-resume");
            startInfo.ArgumentList.Add("--state");
            startInfo.ArgumentList.Add(plan.WebResumePath);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new WebResumeLaunch(
                    null,
                    "The updated Web interface could not be started.");
            }

            return new WebResumeLaunch(process.Id, null);
        }
        catch (Exception ex)
        {
            return new WebResumeLaunch(
                null,
                $"Could not restart the Web interface: {ex.Message}");
        }
    }

    private static string? FindSetsid()
    {
        foreach (var candidate in new[] { "/usr/bin/setsid", "/bin/setsid" })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }
}
