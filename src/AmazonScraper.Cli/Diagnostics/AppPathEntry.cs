namespace AmazonScraper.Cli.Diagnostics;

public enum AppPathKind
{
    File,
    Directory,
    External,
}

public sealed class AppPathEntry
{
    public required string Path { get; init; }

    public required string Kind { get; init; }

    public required string Category { get; init; }

    public string? GroupId { get; init; }

    public string? Description { get; init; }

    public bool Exists { get; init; }

    public long? Size { get; init; }

    public DateTimeOffset? ModifiedAt { get; init; }

    public string Status { get; init; } = "ok";

    public bool Required { get; init; }
}

public sealed class AppPathGroup
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string BasePath { get; init; }

    public string? Description { get; init; }

    public string Status { get; init; } = "ok";

    public List<AppPathEntry> Entries { get; init; } = [];
}

public sealed class AppPathSummary
{
    public int Files { get; set; }

    public int Directories { get; set; }

    public int External { get; set; }

    public int Missing { get; set; }

    public int Stale { get; set; }

    public int Empty { get; set; }

    public int Unreadable { get; set; }
}

public sealed class AppPathCatalogResult
{
    public required string Os { get; init; }

    public required string StateRoot { get; init; }

    public required string InstallDir { get; init; }

    public required string GeneratedAt { get; init; }

    public List<AppPathGroup> Groups { get; init; } = [];

    public AppPathSummary Summary { get; init; } = new();
}
