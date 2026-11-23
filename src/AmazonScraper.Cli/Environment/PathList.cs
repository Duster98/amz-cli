namespace AmazonScraper.Cli.EnvSetup;

/// <summary>
/// Pure helper for Windows PATH strings. Kept independent from the registry so
/// it can be tested on any platform.
/// </summary>
public static class PathList
{
    public static bool Contains(string? path, string entry) =>
        Split(path).Any(item => Same(item, entry));

    public static string Add(string? path, string entry, out bool added)
    {
        var normalizedEntry = entry.Trim();
        var parts = Split(path).ToList();
        if (parts.Any(item => Same(item, normalizedEntry)))
        {
            added = false;
            return string.Join(';', parts);
        }

        parts.Add(normalizedEntry);
        added = true;
        return string.Join(';', parts);
    }

    public static string Remove(string? path, string entry, out bool removed)
    {
        var parts = Split(path).ToList();
        var kept = parts.Where(item => !Same(item, entry)).ToList();
        removed = kept.Count != parts.Count;
        return string.Join(';', kept);
    }

    private static IEnumerable<string> Split(string? path) =>
        (path ?? string.Empty)
            .Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item));

    private static bool Same(string left, string right)
    {
        static string Normalize(string value) =>
            value.Trim().Trim('"').TrimEnd('\\', '/');

        return string.Equals(
            Normalize(left),
            Normalize(right),
            StringComparison.OrdinalIgnoreCase);
    }
}
