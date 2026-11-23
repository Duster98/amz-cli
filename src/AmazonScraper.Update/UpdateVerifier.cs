using System.Diagnostics;

namespace AmazonScraper.Update;

public sealed record VerifyResult(bool Success, string Detail, string Output);

public static class UpdateVerifier
{
    public static async Task<VerifyResult> VerifyAsync(
        string executablePath,
        string expectedVersion,
        string expectedPlatform,
        bool requireCurl,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(executablePath))
        {
            return new VerifyResult(
                false,
                $"Executable not found: {executablePath}",
                string.Empty);
        }

        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory,
        };
        startInfo.ArgumentList.Add("__update-verify");
        startInfo.ArgumentList.Add("--expected-version");
        startInfo.ArgumentList.Add(expectedVersion);
        startInfo.ArgumentList.Add("--expected-platform");
        startInfo.ArgumentList.Add(expectedPlatform);
        if (requireCurl)
        {
            startInfo.ArgumentList.Add("--require-curl");
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return new VerifyResult(false, "Failed to start the new binary.", string.Empty);
            }
        }
        catch (Exception ex)
        {
            return new VerifyResult(
                false,
                $"Unable to start the new binary: {ex.Message}",
                string.Empty);
        }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // Best effort.
            }
            return new VerifyResult(
                false,
                "The new binary test timed out after 30 seconds.",
                string.Empty);
        }

        var output = (await stdout.ConfigureAwait(false)).Trim();
        var error = (await stderr.ConfigureAwait(false)).Trim();
        var detail = string.IsNullOrWhiteSpace(error) ? output : error;
        if (process.ExitCode != 0)
        {
            return new VerifyResult(
                false,
                string.IsNullOrWhiteSpace(detail)
                    ? $"The new binary exited with code {process.ExitCode}."
                    : detail,
                output);
        }

        return new VerifyResult(true, string.IsNullOrWhiteSpace(detail) ? "OK" : detail, output);
    }

    public static async Task WaitForProcessExitAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            // The parent already exited.
        }
        catch (OperationCanceledException)
        {
            throw new UpdateException(
                "The parent process is still running; update cancelled.");
        }
    }
}
