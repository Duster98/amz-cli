using System.Text.Json.Serialization;

namespace AmazonScraper.Cli.Diagnostics;

public enum DoctorStatus
{
    Ok,
    Info,
    Warning,
    Error,
}

public sealed record DoctorCheck(
    string Id,
    string Title,
    DoctorStatus Status,
    string Detail);

public sealed record DoctorReport(
    string Version,
    string Platform,
    string InstallDir,
    string StateRoot,
    IReadOnlyList<DoctorCheck> Checks)
{
    [JsonIgnore]
    public bool HasErrors => Checks.Any(check => check.Status == DoctorStatus.Error);

    [JsonIgnore]
    public bool HasWarnings => Checks.Any(check => check.Status == DoctorStatus.Warning);

    [JsonPropertyName("error_count")]
    public int ErrorCount => Checks.Count(check => check.Status == DoctorStatus.Error);

    [JsonPropertyName("warning_count")]
    public int WarningCount => Checks.Count(check => check.Status == DoctorStatus.Warning);
}
