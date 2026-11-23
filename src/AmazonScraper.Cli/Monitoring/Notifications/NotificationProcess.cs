using System.Diagnostics;

namespace AmazonScraper.Cli.Monitoring.Notifications;

internal static class NotificationProcess
{
    public static async Task<NotificationSendResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new NotificationSendResult(
                    false,
                    Error: "could not start the notification process");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var exitTask = process.WaitForExitAsync(cancellationToken);
            var timeoutTask = Task.Delay(timeout, cancellationToken);
            var completed = await Task.WhenAny(exitTask, timeoutTask).ConfigureAwait(false);

            if (completed != exitTask)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                    // Best effort.
                }

                cancellationToken.ThrowIfCancellationRequested();
                return new NotificationSendResult(
                    false,
                    Error: $"timeout after {timeout.TotalSeconds:0} seconds");
            }

            await exitTask.ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            if (process.ExitCode == 0)
            {
                return new NotificationSendResult(
                    true,
                    string.IsNullOrWhiteSpace(stdout) ? null : stdout.Trim());
            }

            var error = string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim();
            return new NotificationSendResult(
                false,
                Error: string.IsNullOrWhiteSpace(error)
                    ? $"exit code {process.ExitCode}"
                    : error);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new NotificationSendResult(false, Error: ex.Message);
        }
    }
}
