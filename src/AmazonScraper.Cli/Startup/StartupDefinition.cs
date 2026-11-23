using System.Text.Json.Serialization;

namespace AmazonScraper.Cli.Startup;

public sealed class StartupDefinition
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = string.Empty;

    [JsonPropertyName("executable")]
    public string Executable { get; set; } = string.Empty;

    [JsonPropertyName("prefix_arguments")]
    public List<string> PrefixArguments { get; set; } = [];

    [JsonPropertyName("working_directory")]
    public string WorkingDirectory { get; set; } = string.Empty;

    [JsonPropertyName("config_path")]
    public string? ConfigPath { get; set; }

    [JsonPropertyName("binding_id")]
    public string? BindingId { get; set; }

    [JsonPropertyName("save_path")]
    public string SavePath { get; set; } = string.Empty;

    [JsonPropertyName("save_file")]
    public string SaveFile { get; set; } = "amz-monitor";

    [JsonPropertyName("domains")]
    public List<string> Domains { get; set; } = [];

    [JsonPropertyName("targets")]
    public int Targets { get; set; }

    [JsonPropertyName("timer_seconds")]
    public int TimerSeconds { get; set; }

    [JsonPropertyName("schedule_mode")]
    public string? ScheduleMode { get; set; }

    [JsonPropertyName("window_seconds")]
    public int? WindowSeconds { get; set; }

    [JsonPropertyName("max_requests")]
    public int? MaxRequests { get; set; }

    [JsonPropertyName("min_gap_seconds")]
    public int? MinGapSeconds { get; set; }

    [JsonPropertyName("max_gap_seconds")]
    public int? MaxGapSeconds { get; set; }

    [JsonPropertyName("data_files")]
    public List<string> DataFiles { get; set; } = [];

    [JsonPropertyName("log_path")]
    public string? LogPath { get; set; }

    [JsonPropertyName("arguments")]
    public List<string> Arguments { get; set; } = [];

    [JsonPropertyName("command_preview")]
    public string CommandPreview { get; set; } = string.Empty;
}
