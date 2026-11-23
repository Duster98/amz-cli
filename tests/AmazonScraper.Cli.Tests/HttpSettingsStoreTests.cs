using AmazonScraper.Cli.Storage;

namespace AmazonScraper.Cli.Tests;

public class HttpSettingsStoreTests
{
    [Fact]
    public void Missing_file_returns_automatic_defaults()
    {
        using var directory = new TempDirectory();
        var store = new HttpSettingsStore(
            Path.Combine(directory.Root, "http-settings.json"));

        var settings = store.Load();

        Assert.Equal("auto", settings.Transport);
        Assert.Equal("auto", settings.Browser);
        Assert.Equal(string.Empty, settings.UserAgent);
        Assert.False(settings.BrowserHeaders);
        Assert.Equal(30, settings.TimeoutSeconds);
        Assert.True(settings.FallbackToCurl);
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void Save_and_load_roundtrip_normalizes_values()
    {
        using var directory = new TempDirectory();
        var store = new HttpSettingsStore(
            Path.Combine(directory.Root, "http-settings.json"));

        var saved = store.Save(new HttpSettings
        {
            Transport = " CURL ",
            Browser = "firefox147",
            UserAgent = "  Mozilla/5.0 (Test)\n  ",
            BrowserHeaders = true,
            TimeoutSeconds = 1000,
            FallbackToCurl = false,
        });

        Assert.Equal("curl", saved.Transport);
        Assert.Equal("Firefox147", saved.Browser);
        Assert.Equal("Mozilla/5.0 (Test)", saved.UserAgent);
        Assert.True(saved.BrowserHeaders);
        Assert.Equal(120, saved.TimeoutSeconds);
        Assert.False(saved.FallbackToCurl);
        Assert.True(File.Exists(store.FilePath));

        var loaded = store.Load();
        Assert.Equal("curl", loaded.Transport);
        Assert.Equal("Firefox147", loaded.Browser);
        Assert.Equal("Mozilla/5.0 (Test)", loaded.UserAgent);
        Assert.True(loaded.BrowserHeaders);
        Assert.Equal(120, loaded.TimeoutSeconds);
        Assert.False(loaded.FallbackToCurl);
        Assert.Equal(saved.UpdatedAt, loaded.UpdatedAt);
    }

    [Fact]
    public void Invalid_values_fall_back_to_automatic_defaults()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Root, "http-settings.json");
        File.WriteAllText(
            path,
            """
            {
              "schema": 99,
              "transport": "carrier-pigeon",
              "browser": "NotABrowser",
              "user_agent": "   ",
              "timeout_seconds": -4,
              "fallback_to_curl": false
            }
            """);
        var store = new HttpSettingsStore(path);

        var settings = store.Load();

        Assert.Equal(HttpSettings.CurrentSchema, settings.Schema);
        Assert.Equal("auto", settings.Transport);
        Assert.Equal("auto", settings.Browser);
        Assert.Equal(string.Empty, settings.UserAgent);
        Assert.Equal(30, settings.TimeoutSeconds);
        Assert.False(settings.FallbackToCurl);
    }

    [Fact]
    public void Corrupt_file_is_replaced_by_defaults_in_memory()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Root, "http-settings.json");
        File.WriteAllText(path, "{ not json");
        var store = new HttpSettingsStore(path);

        var settings = store.Load();

        Assert.Equal("auto", settings.Transport);
        Assert.Equal(30, settings.TimeoutSeconds);
    }

    [Fact]
    public void Reset_writes_the_defaults()
    {
        using var directory = new TempDirectory();
        var store = new HttpSettingsStore(
            Path.Combine(directory.Root, "http-settings.json"));
        store.Save(new HttpSettings
        {
            Transport = "http",
            Browser = "Safari260Ios",
            UserAgent = "Custom",
            TimeoutSeconds = 90,
        });

        var reset = store.Reset();

        Assert.Equal("auto", reset.Transport);
        Assert.Equal("auto", reset.Browser);
        Assert.Equal(string.Empty, reset.UserAgent);
        Assert.Equal(30, reset.TimeoutSeconds);
        Assert.True(reset.FallbackToCurl);
        Assert.Equal("auto", store.Load().Transport);
    }

    [Fact]
    public void Timeout_and_user_agent_are_clamped_to_safe_values()
    {
        var tooShort = new HttpSettings { TimeoutSeconds = 0 }.Normalize();
        var tooLong = new HttpSettings { TimeoutSeconds = 500 }.Normalize();
        var nan = new HttpSettings { TimeoutSeconds = double.NaN }.Normalize();
        var longAgent = new HttpSettings
        {
            UserAgent = new string('a', 300),
        }.Normalize();

        Assert.Equal(30, tooShort.TimeoutSeconds);
        Assert.Equal(120, tooLong.TimeoutSeconds);
        Assert.Equal(30, nan.TimeoutSeconds);
        Assert.Equal(256, longAgent.UserAgent.Length);
    }

    [Fact]
    public void Browser_names_are_exposed_for_the_web_selector()
    {
        var profiles = HttpSettings.AvailableBrowsers;

        Assert.Contains("Firefox147", profiles);
        Assert.Contains("Safari260Ios", profiles);
        Assert.Contains("Chrome146", profiles);
    }

    [Fact]
    public void Saved_file_is_private_on_unix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TempDirectory();
        var store = new HttpSettingsStore(
            Path.Combine(directory.Root, "http-settings.json"));
        store.Save(new HttpSettings { Transport = "curl" });

        var mode = File.GetUnixFileMode(store.FilePath);
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            mode & (UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupWrite
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherWrite));
    }
}
