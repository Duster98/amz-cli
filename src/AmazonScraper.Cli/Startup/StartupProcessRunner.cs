using System.Diagnostics;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Startup;

public sealed record StartupProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public bool Success => ExitCode == 0;

    public string Detail()
    {
        var value = string.Join(
            Environment.NewLine,
            new[] { StandardOutput, StandardError }
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => text.Trim()));
        return string.IsNullOrWhiteSpace(value)
            ? $"exit code {ExitCode}"
            : value;
    }
}

public interface IStartupProcessRunner
{
    StartupProcessResult Run(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null);
}

public sealed class StartupProcessRunner : IStartupProcessRunner
{
    public StartupProcessResult Run(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            throw new MonitorException(
                $"Could not run '{fileName}': {ex.Message}");
        }

        if (process is null)
        {
            throw new MonitorException(
                $"Could not run '{fileName}'.");
        }

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            var limit = timeout ?? TimeSpan.FromSeconds(30);
            if (!process.WaitForExit((int)limit.TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                    // Best effort.
                }

                throw new MonitorException(
                    $"Timeout after {limit.TotalSeconds:0}s running '{fileName}'.");
            }

            Task.WaitAll(stdout, stderr);
            return new StartupProcessResult(
                process.ExitCode,
                stdout.Result,
                stderr.Result);
        }
    }
}
