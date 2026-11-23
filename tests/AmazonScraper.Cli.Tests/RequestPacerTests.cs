using AmazonScraper.Cli.Monitoring;

namespace AmazonScraper.Cli.Tests;

public class RequestPacerTests
{
    [Fact]
    public void Delay_is_the_remaining_part_of_the_interval()
    {
        var delay = RequestPacer.ComputeDelay(
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(7), delay);
    }

    [Fact]
    public void No_delay_when_the_interval_has_already_elapsed()
    {
        var delay = RequestPacer.ComputeDelay(
            TimeSpan.FromSeconds(12),
            TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.Zero, delay);
    }
}
