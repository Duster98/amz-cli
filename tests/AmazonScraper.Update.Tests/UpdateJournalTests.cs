using AmazonScraper.Update;

namespace AmazonScraper.Update.Tests;

public class UpdateJournalTests
{
    [Fact]
    public void Journal_roundtrip_preserves_release_notes()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "journal.json");
        var journal = new UpdateJournal
        {
            Phase = UpdatePhase.Completed,
            FromVersion = "2.27.5",
            ToVersion = "2.27.6",
            Notes = "What's new\n- first item\n- second item",
        };

        UpdateJournalStore.Write(journal, path);
        var read = UpdateJournalStore.Read(path);

        Assert.NotNull(read);
        Assert.Equal(journal.Notes, read!.Notes);
    }
}

public class UpdateApplyPlanWebRestartTests
{
    [Fact]
    public void Plan_roundtrip_preserves_web_restart_metadata()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "plan.json");
        var plan = new UpdateApplyPlan
        {
            InstallDir = "/tmp/install",
            Platform = "linux-x64",
            FromVersion = "3.1.2",
            ToVersion = "3.2.0",
            RestartWeb = true,
            WebResumePath = "/tmp/web-resume.json",
            ForceMonitorRestart = true,
        };

        UpdateApplyPlanStore.Write(path, plan);
        var read = UpdateApplyPlanStore.Read(path);

        Assert.True(read.RestartWeb);
        Assert.True(read.ForceMonitorRestart);
        Assert.Equal("/tmp/web-resume.json", read.WebResumePath);
    }
}
