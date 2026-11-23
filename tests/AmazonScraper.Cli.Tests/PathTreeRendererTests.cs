using AmazonScraper.Cli.Diagnostics;

namespace AmazonScraper.Cli.Tests;

public class PathTreeRendererTests
{
    [Fact]
    public void Tree_uses_unicode_and_ascii_prefixes()
    {
        var result = Result();

        var unicode = PathTreeRenderer.Render(result, verbose: false, ascii: false);
        Assert.Contains("├── ", unicode);
        Assert.Contains("└── ", unicode);
        Assert.Contains("amz-monitor.state.json", unicode);

        var ascii = PathTreeRenderer.Render(result, verbose: false, ascii: true);
        Assert.Contains("|-- ", ascii);
        Assert.Contains("\\-- ", ascii);
    }

    [Fact]
    public void Tree_shows_status_and_summary()
    {
        var text = PathTreeRenderer.Render(
            Result(),
            verbose: true,
            ascii: false);

        Assert.Contains("[stale]", text);
        Assert.Contains("[missing]", text);
        Assert.Contains("Summary:", text);
        Assert.Contains("1 file", text);
    }

    [Fact]
    public void Json_is_snake_case()
    {
        var json = PathTreeRenderer.ToJson(Result());

        Assert.Contains("\"state_root\"", json);
        Assert.Contains("\"generated_at\"", json);
        Assert.Contains("\"base_path\"", json);
        Assert.Contains("\"modified_at\"", json);
    }

    private static AppPathCatalogResult Result() =>
        new()
        {
            Os = "linux-x64",
            StateRoot = "/tmp/state",
            InstallDir = "/tmp/install",
            GeneratedAt = "2026-11-17T00:00:00Z",
            Groups =
            [
                new AppPathGroup
                {
                    Id = "monitor:test",
                    Title = "Monitor test",
                    BasePath = "/tmp/monitor",
                    Status = "stale",
                    Entries =
                    [
                        new AppPathEntry
                        {
                            Path = "/tmp/monitor/amz-monitor.state.json",
                            Kind = "file",
                            Category = "monitor-data",
                            Exists = true,
                            Size = 19,
                            ModifiedAt = DateTimeOffset.Parse("2026-11-17T00:00:00Z"),
                            Status = "stale",
                        },
                        new AppPathEntry
                        {
                            Path = "/tmp/monitor/amz-monitor.log",
                            Kind = "file",
                            Category = "monitor-data",
                            Exists = false,
                            Status = "missing",
                        },
                    ],
                },
                new AppPathGroup
                {
                    Id = "external",
                    Title = "Registrazioni esterne",
                    BasePath = "",
                    Entries =
                    [
                        new AppPathEntry
                        {
                            Path = "/tmp/systemd/amz-monitor-test.service",
                            Kind = "external",
                            Category = "external",
                            Exists = true,
                            Status = "ok",
                            Description = "systemd user",
                        },
                    ],
                },
            ],
            Summary = new AppPathSummary
            {
                Files = 1,
                Missing = 1,
                Stale = 1,
            },
        };
}
