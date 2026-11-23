using AmazonScraper.Cli.Storage;

namespace AmazonScraper.Cli.Tests;

public class TransportCooldownStoreTests
{
    [Fact]
    public void Missing_state_does_not_create_a_cooldown()
    {
        using var directory = new TempDirectory();
        var store = new TransportCooldownStore(directory.Root);

        Assert.False(store.IsCurlPreferred("com"));
        Assert.Null(store.GetActive("com"));
        Assert.Empty(store.ListActive());
    }

    [Fact]
    public void Http_failure_is_persisted_for_the_default_two_hours()
    {
        using var directory = new TempDirectory();
        var store = new TransportCooldownStore(directory.Root);
        var now = DateTimeOffset.UtcNow;

        Assert.True(store.RememberHttpFailure(
            "com",
            "HTTP 503",
            now: now));

        var active = store.GetActive("com", now.AddMinutes(1));
        Assert.NotNull(active);
        Assert.Equal("com", active!.Domain);
        Assert.Contains("503", active.Reason);
        Assert.InRange(
            active.Until,
            now.AddHours(2).AddSeconds(-1),
            now.AddHours(2).AddSeconds(1));
        Assert.True(store.IsCurlPreferred("com", now.AddMinutes(1)));
        Assert.True(File.Exists(Path.Combine(directory.Root, "com.json")));
    }

    [Fact]
    public void Expired_cooldown_is_ignored()
    {
        using var directory = new TempDirectory();
        var store = new TransportCooldownStore(directory.Root);
        var now = DateTimeOffset.UtcNow;

        Assert.True(store.RememberHttpFailure(
            "com",
            "HTTP 503",
            TimeSpan.FromHours(1),
            now.AddHours(-2)));

        Assert.False(store.IsCurlPreferred("com", now));
        Assert.Null(store.GetActive("com", now));
        Assert.Empty(store.ListActive(now));
    }

    [Fact]
    public void Cooldowns_are_isolated_per_marketplace()
    {
        using var directory = new TempDirectory();
        var store = new TransportCooldownStore(directory.Root);
        var now = DateTimeOffset.UtcNow;

        Assert.True(store.RememberHttpFailure("com", "HTTP 503", now: now));
        Assert.True(store.RememberHttpFailure("es", "HTTP 429", now: now));

        Assert.True(store.IsCurlPreferred("com", now));
        Assert.True(store.IsCurlPreferred("es", now));
        Assert.False(store.IsCurlPreferred("it", now));
        Assert.Equal(
            new[] { "com", "es" },
            store.ListActive(now).Select(item => item.Domain).ToArray());
    }

    [Fact]
    public void Corrupt_files_fall_back_without_throwing()
    {
        using var directory = new TempDirectory();
        var store = new TransportCooldownStore(directory.Root);
        Directory.CreateDirectory(directory.Root);
        File.WriteAllText(
            Path.Combine(directory.Root, "com.json"),
            "{ this is not valid json");

        Assert.False(store.IsCurlPreferred("com"));
        Assert.Null(store.GetActive("com"));
        Assert.Empty(store.ListActive());
    }

    [Fact]
    public void Clear_removes_a_marketplace_cooldown()
    {
        using var directory = new TempDirectory();
        var store = new TransportCooldownStore(directory.Root);

        Assert.True(store.RememberHttpFailure("com", "HTTP 503"));
        Assert.True(store.IsCurlPreferred("com"));

        store.Clear("com");

        Assert.False(store.IsCurlPreferred("com"));
    }

    [Fact]
    public void State_files_are_private_on_unix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TempDirectory();
        var store = new TransportCooldownStore(directory.Root);

        Assert.True(store.RememberHttpFailure("com", "HTTP 503"));
        var file = Path.Combine(directory.Root, "com.json");

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(file));
        Assert.Equal(
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute,
            File.GetUnixFileMode(directory.Root));
    }
}
