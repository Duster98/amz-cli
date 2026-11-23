namespace AmazonScraper.Update;

public static class InstallLocation
{
    /// <summary>
    /// Directory that contains the running executable. On Linux a PATH entry
    /// may be a symlink; the update must replace the target, not the link.
    /// </summary>
    public static string Resolve()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(AppContext.BaseDirectory));
        }

        var fullPath = Path.GetFullPath(processPath);
        try
        {
            var resolved = File.ResolveLinkTarget(fullPath, returnFinalTarget: true);
            if (resolved is not null)
            {
                fullPath = resolved.FullName;
            }
        }
        catch (Exception)
        {
            // A non-symlink executable is the normal case.
        }

        return Path.GetDirectoryName(fullPath)
            ?? throw new UpdateException(
                $"Unable to determine the installation directory from '{fullPath}'.");
    }

    public static string ExecutablePath(string installDir, string platform) =>
        Path.Combine(installDir, PlatformInfo.ExecutableName(platform));
}
