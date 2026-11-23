using AmazonScraper.Cli.Commands;
using AmazonScraper.Cli.Storage;

namespace AmazonScraper.Cli.Tests;

public class SettingsCommandTests
{
    [Fact]
    public void Set_requires_at_least_one_value()
    {
        var result = new SettingsSetSettings().Validate();

        Assert.False(result.Successful);
    }

    [Fact]
    public void Set_rejects_invalid_transport_browser_timeout_and_flags()
    {
        Assert.False(new SettingsSetSettings
        {
            Transport = "ftp",
        }.Validate().Successful);
        Assert.False(new SettingsSetSettings
        {
            Browser = "NotABrowser",
        }.Validate().Successful);
        Assert.False(new SettingsSetSettings
        {
            Timeout = 2,
        }.Validate().Successful);
        Assert.False(new SettingsSetSettings
        {
            BrowserHeaders = true,
            NoBrowserHeaders = true,
        }.Validate().Successful);
        Assert.False(new SettingsSetSettings
        {
            Fallback = true,
            NoFallback = true,
        }.Validate().Successful);
        Assert.False(new SettingsSetSettings
        {
            UserAgent = "bad\nvalue",
        }.Validate().Successful);
    }

    [Fact]
    public void Set_accepts_auto_values()
    {
        Assert.True(new SettingsSetSettings
        {
            Transport = "auto",
            Browser = "auto",
            Timeout = 30,
        }.Validate().Successful);
    }

    [Fact]
    public void Set_command_persists_and_normalizes_the_values()
    {
        using var directory = new TempDirectory();
        var store = new HttpSettingsStore(
            Path.Combine(directory.Root, "http-settings.json"));
        var command = new SettingsSetCommand(store);

        var exit = command.Execute(
            null!,
            new SettingsSetSettings
            {
                Transport = "CURL",
                Browser = "firefox147",
                UserAgent = " Test UA ",
                BrowserHeaders = true,
                Timeout = 45,
                NoFallback = true,
            },
            CancellationToken.None);

        Assert.Equal(0, exit);
        var saved = store.Load();
        Assert.Equal("curl", saved.Transport);
        Assert.Equal("Firefox147", saved.Browser);
        Assert.Equal("Test UA", saved.UserAgent);
        Assert.True(saved.BrowserHeaders);
        Assert.Equal(45, saved.TimeoutSeconds);
        Assert.False(saved.FallbackToCurl);
    }

    [Fact]
    public void Set_command_can_clear_the_user_agent()
    {
        using var directory = new TempDirectory();
        var store = new HttpSettingsStore(
            Path.Combine(directory.Root, "http-settings.json"));
        store.Save(new HttpSettings { UserAgent = "Custom" });

        var exit = new SettingsSetCommand(store).Execute(
            null!,
            new SettingsSetSettings { UserAgent = "auto" },
            CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, store.Load().UserAgent);
    }

    [Fact]
    public void Reset_command_restores_automatic_defaults()
    {
        using var directory = new TempDirectory();
        var store = new HttpSettingsStore(
            Path.Combine(directory.Root, "http-settings.json"));
        store.Save(new HttpSettings
        {
            Transport = "http",
            Browser = "Safari260Ios",
            TimeoutSeconds = 90,
        });

        var exit = new SettingsResetCommand(store).Execute(
            null!,
            new SettingsResetSettings(),
            CancellationToken.None);

        Assert.Equal(0, exit);
        var reset = store.Load();
        Assert.Equal("auto", reset.Transport);
        Assert.Equal("auto", reset.Browser);
        Assert.Equal(30, reset.TimeoutSeconds);
    }
}
