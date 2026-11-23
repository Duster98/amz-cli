using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Core.Tests;

public class MonitorNotificationPayloadTests
{
    [Fact]
    public void Update_payload_carries_details_for_the_viewer()
    {
        var now = DateTimeOffset.UtcNow;

        var payload = MonitorNotificationPayloadFactory.Update(
            "Amazon aggiornato a 2.27.0",
            "Aggiornamento completato.",
            "completed",
            "2.26.3",
            "2.27.0",
            true,
            "/tmp/backup.tar.gz",
            "/tmp/update.log",
            null,
            "startup",
            now,
            "Titolo\n- punto");

        Assert.Equal("update", payload.Kind);
        Assert.Equal("completed", payload.UpdateStatus);
        Assert.Equal("2.26.3", payload.FromVersion);
        Assert.Equal("2.27.0", payload.ToVersion);
        Assert.True(payload.MonitorsRestarted);
        Assert.Equal("/tmp/backup.tar.gz", payload.BackupPath);
        Assert.Equal("startup", payload.Trigger);
        Assert.Equal("Titolo\n- punto", payload.Notes);
        Assert.NotNull(payload.CreatedAt);
    }

    [Fact]
    public void Update_payload_can_omit_monitor_state()
    {
        var payload = MonitorNotificationPayloadFactory.Update(
            "Aggiornato",
            "Nessun monitor.",
            "completed",
            "2.26.3",
            "2.27.0",
            null,
            null,
            null,
            null,
            "monitor",
            DateTimeOffset.UtcNow);

        Assert.Null(payload.MonitorsRestarted);
    }
}
