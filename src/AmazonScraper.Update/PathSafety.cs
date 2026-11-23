namespace AmazonScraper.Update;

public static class PathSafety
{
    public const int MaxRelativePathLength = 512;

    /// <summary>
    /// Validates a path coming from a signed release manifest or an install
    /// state. The path must stay below the installation root on every OS.
    /// </summary>
    public static bool IsSafeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.Length > MaxRelativePathLength
            || path.Contains('\0')
            || path.Contains('\\')
            || path.StartsWith('/')
            || path.StartsWith('~')
            || path.Contains("..", StringComparison.Ordinal)
            || (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':'))
        {
            return false;
        }

        var segments = path.Split('/');
        return segments.All(segment =>
            segment.Length > 0
            && segment != "."
            && segment != "..");
    }

    public static string CombineSafe(string root, string relativePath)
    {
        if (!IsSafeRelativePath(relativePath))
        {
            throw new UpdateException($"Percorso non sicuro nel manifest: '{relativePath}'.");
        }

        var fullRoot = Path.GetFullPath(root);
        var combined = Path.GetFullPath(
            Path.Combine(
                fullRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));

        var rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new UpdateException($"Path is outside the installation: '{relativePath}'.");
        }

        return combined;
    }
}
