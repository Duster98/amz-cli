using AmazonScraper.Cli.Web;
using AmazonScraper.Update;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmazonScraper.Cli.Tests;

public class UpdateJobManagerTests
{
    [Fact]
    public async Task Check_and_apply_produce_update_events_and_resume_state()
    {
        using var directory = new TempDirectory();
        var resumePath = Path.Combine(directory.Root, "resume.json");
        var lifetime = new FakeLifetime();
        var notifier = new FakeNotifier();
        var executor = new FakeExecutor
        {
            Check = CreateCheck(UpdateStatus.UpdateAvailable),
        };
        var jobs = new SearchJobManager(
            NullLogger.Instance,
            lifetime);
        using var manager = new UpdateJobManager(
            executor,
            lifetime,
            notifier,
            jobs,
            new WebOptions
            {
                Port = 12345,
                Remote = true,
                LogPath = Path.Combine(directory.Root, "web.log"),
            },
            new WebSessionState("instance-1", "token-1") { Port = 12345 },
            NullLogger.Instance,
            resumePath);

        Assert.True(manager.TryStartCheck(out var checkError), checkError);
        await WaitForPhaseAsync(manager, "update_available");
        Assert.False(string.IsNullOrWhiteSpace(manager.GetStatus().Check?.LatestVersion));

        Assert.True(manager.TryStartApply(out var applyError), applyError);
        await WaitForPhaseAsync(manager, "scheduled");
        Assert.True(File.Exists(resumePath));
        var resume = WebResumeStateStore.TryRead(resumePath);
        Assert.NotNull(resume);
        Assert.Equal(12345, resume!.Port);
        Assert.Equal("token-1", resume.Token);
        Assert.Equal("instance-1", resume.InstanceId);
        Assert.True(resume.Remote);

        await Task.Delay(1400);
        Assert.Equal(1, lifetime.StopCount);
        Assert.Contains(notifier.Changes, item => item.Phase == "update_available");
        Assert.Contains(notifier.Changes, item => item.Phase == "preparing");
        Assert.Contains(notifier.Changes, item => item.Phase == "scheduled");
    }

    [Fact]
    public async Task A_second_operation_is_rejected_while_checking()
    {
        using var directory = new TempDirectory();
        var lifetime = new FakeLifetime();
        var gate = new TaskCompletionSource<UpdateCheckResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new FakeExecutor { CheckTask = gate.Task };
        var jobs = new SearchJobManager(NullLogger.Instance, lifetime);
        using var manager = new UpdateJobManager(
            executor,
            lifetime,
            new FakeNotifier(),
            jobs,
            new WebOptions { LogPath = Path.Combine(directory.Root, "web.log") },
            new WebSessionState { Port = 12345 },
            NullLogger.Instance,
            Path.Combine(directory.Root, "resume.json"));

        Assert.True(manager.TryStartCheck(out _));
        Assert.False(manager.TryStartCheck(out var secondError));
        Assert.Equal("update_busy", secondError);
        gate.SetResult(CreateCheck(UpdateStatus.UpToDate));
        await WaitForPhaseAsync(manager, "up_to_date");
    }

    [Fact]
    public void Resume_state_round_trips_and_is_deleted()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Root, "resume.json");
        var state = new WebResumeState
        {
            Port = 43210,
            Token = "token",
            InstanceId = "instance",
            Desktop = true,
            Remote = true,
            Development = true,
            WebDirectory = "/tmp/web",
            IdleMinutes = 7,
            LogPath = "/tmp/web.log",
        };

        WebResumeStateStore.Write(state, path);
        var read = WebResumeStateStore.TryRead(path);

        Assert.NotNull(read);
        Assert.Equal(43210, read!.Port);
        Assert.Equal("token", read.Token);
        Assert.Equal("instance", read.InstanceId);
        Assert.True(read.Desktop);
        Assert.True(read.Remote);
        Assert.True(read.Development);
        Assert.Equal(7, read.IdleMinutes);
        WebResumeStateStore.Delete(path);
        Assert.Null(WebResumeStateStore.TryRead(path));
    }

    private static async Task WaitForPhaseAsync(
        UpdateJobManager manager,
        string phase)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (manager.Snapshot.Phase == phase)
            {
                return;
            }
            await Task.Delay(50);
        }
        Assert.Equal(phase, manager.Snapshot.Phase);
    }

    private static UpdateCheckResult CreateCheck(UpdateStatus status) =>
        new(
            status,
            "3.2.0",
            "linux-x64",
            "stable",
            null,
            new ReleaseManifest
            {
                Schema = 2,
                Version = "9.9.9",
                Platform = "linux-x64",
                Channel = "stable",
                PublishedAt = DateTimeOffset.UtcNow,
                Files =
                [
                    new ReleaseFile
                    {
                        Path = "amz-cli",
                        Size = 10,
                        Sha256 = new string('a', 64),
                        DownloadPath = "stable/linux-x64/9.9.9/amz-cli",
                        DownloadSize = 10,
                        DownloadSha256 = new string('a', 64),
                    },
                ],
            },
            status == UpdateStatus.UpdateAvailable
                ? "Update available: 3.2.0 → 9.9.9."
                : "Version 3.2.0 is already up to date.");

    private sealed class FakeExecutor : IUpdateExecutor
    {
        public UpdateCheckResult Check { get; set; } =
            CreateCheck(UpdateStatus.UpToDate);

        public Task<UpdateCheckResult>? CheckTask { get; set; }

        public Task<UpdateCheckResult> CheckAsync(
            string channel,
            CancellationToken cancellationToken) =>
            CheckTask ?? Task.FromResult(Check);

        public Task<UpdateApplyOutcome> ApplyAsync(
            UpdateCheckResult check,
            bool requireCurl,
            IProgress<UpdateProgress>? progress,
            CancellationToken cancellationToken,
            AutoUpdateContext auto,
            WebUpdateContext web) =>
            Task.FromResult(new UpdateApplyOutcome(
                UpdateApplyState.Scheduled,
                "/tmp/backup.tar.gz",
                "/tmp/helper",
                null,
                "Update scheduled."));
    }

    private sealed class FakeNotifier : IUpdateRealtimeNotifier
    {
        public List<UpdateChangedEvent> Changes { get; } = [];

        public List<UpdateProgressEvent> Progress { get; } = [];

        public Task NotifyChangedAsync(
            UpdateChangedEvent change,
            CancellationToken cancellationToken = default)
        {
            lock (Changes)
            {
                Changes.Add(change);
            }
            return Task.CompletedTask;
        }

        public Task NotifyProgressAsync(
            UpdateProgressEvent progress,
            CancellationToken cancellationToken = default)
        {
            lock (Progress)
            {
                Progress.Add(progress);
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public int StopCount { get; private set; }

        public void StopApplication()
        {
            StopCount++;
            _stopping.Cancel();
        }
    }
}
