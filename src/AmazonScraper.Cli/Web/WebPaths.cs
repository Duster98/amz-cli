using AmazonScraper.Cli.Storage;

namespace AmazonScraper.Cli.Web;

public static class WebPaths
{
    public static string Root() => Path.Combine(UserStatePaths.Root(), "web");

    public static string SessionPath() => Path.Combine(Root(), "session.json");

    public static string UiStatePath() => Path.Combine(Root(), "ui-state.json");

    public static string LockPath() => Path.Combine(Root(), "session.lock");

    public static string LogPath() => Path.Combine(Root(), "web.log");

    public static string UpdateResumePath() =>
        Path.Combine(Root(), "update-resume.json");

    public static string HistoryCacheDirectory() =>
        Path.Combine(Root(), "history-cache");

    public static string ProductCacheDirectory() =>
        Path.Combine(Root(), "product-cache");

    public static string CacheSettingsPath() =>
        Path.Combine(Root(), "cache-settings.json");

    public static string ComparePath() =>
        Path.Combine(Root(), "compare.json");
}

public sealed class WebException(string message, Exception? inner = null)
    : Exception(message, inner);
