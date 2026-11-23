using System.Diagnostics;

namespace AmazonScraper.Cli.Monitoring;

/// <summary>
/// Enforces a quiet gap between the end of one Amazon request and the start
/// of the next. The pacer is global across all marketplaces in a monitor run.
/// </summary>
public sealed class RequestPacer
{
    private readonly TimeSpan _interval;
    private long? _lastCompletedTimestamp;

    public RequestPacer(int seconds)
    {
        _interval = TimeSpan.FromSeconds(Math.Max(0, seconds));
    }

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        if (_lastCompletedTimestamp is not { } last)
        {
            return;
        }

        var delay = ComputeDelay(Stopwatch.GetElapsedTime(last), _interval);
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    public void MarkCompleted() =>
        _lastCompletedTimestamp = Stopwatch.GetTimestamp();

    public static TimeSpan ComputeDelay(TimeSpan elapsed, TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero || elapsed >= interval)
        {
            return TimeSpan.Zero;
        }

        return interval - elapsed;
    }
}
