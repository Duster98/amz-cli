namespace AmazonScraper.Core.Monitoring;

public sealed class MonitorPaths
{
    public string SavePath { get; }

    public string BaseName { get; }

    public string StatePath => Path.Combine(SavePath, $"{BaseName}.state.json");

    public string LogPath => Path.Combine(SavePath, $"{BaseName}.log");

    public string PidPath => Path.Combine(SavePath, $"{BaseName}.pid");

    public string LockPath => Path.Combine(SavePath, $"{BaseName}.lock");

    public string PerGroupRoot => Path.Combine(SavePath, BaseName);

    private MonitorPaths(string savePath, string baseName)
    {
        SavePath = savePath;
        BaseName = baseName;
    }

    public static MonitorPaths Resolve(string savePath, string? saveFile)
    {
        if (string.IsNullOrWhiteSpace(savePath))
        {
            throw new MonitorException("--save-path cannot be empty.");
        }

        var directory = Path.GetFullPath(savePath.Trim());
        var requested = string.IsNullOrWhiteSpace(saveFile)
            ? "amz-monitor"
            : saveFile.Trim();
        if (requested.Length == 0
            || requested is "." or ".."
            || requested.Contains('/')
            || requested.Contains('\\')
            || requested.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new MonitorException(
                "--save-file must be a simple file name, without directories.");
        }

        var baseName = Path.GetFileName(requested);
        if (string.IsNullOrEmpty(baseName))
        {
            throw new MonitorException("--save-file cannot be empty.");
        }

        if (baseName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            baseName = baseName[..^".jsonl".Length];
        }
        else if (baseName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            baseName = baseName[..^".csv".Length];
        }

        if (baseName.Length == 0)
        {
            throw new MonitorException("--save-file cannot be empty.");
        }

        return new MonitorPaths(directory, baseName);
    }

    public string CombinedPath(string recordType, MonitorFormat format) =>
        Path.Combine(SavePath, $"{BaseName}.{recordType}.{Extension(format)}");

    public string SinglePath(MonitorFormat format) =>
        Path.Combine(SavePath, $"{BaseName}.{Extension(format)}");

    public string PerGroupJsonlPath(string? groupId) =>
        Path.Combine(PerGroupRoot, $"{GroupSlug(groupId)}.jsonl");

    public string PerGroupCsvPath(string? groupId, string recordType) =>
        Path.Combine(PerGroupRoot, GroupSlug(groupId), $"{recordType}.csv");

    public static string Extension(MonitorFormat format) =>
        format == MonitorFormat.Csv ? "csv" : "jsonl";

    private static string GroupSlug(string? groupId) =>
        string.IsNullOrWhiteSpace(groupId) ? "ungrouped" : groupId;
}
