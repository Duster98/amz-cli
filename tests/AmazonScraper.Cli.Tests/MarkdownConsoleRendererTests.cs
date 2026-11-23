using AmazonScraper.Cli.Commands;
using AmazonScraper.Cli.Rendering;
using AmazonScraper.Update;

namespace AmazonScraper.Cli.Tests;

public class MarkdownConsoleRendererTests
{
    [Fact]
    public void Update_check_renders_markdown_notes_without_throwing()
    {
        var manifest = new ReleaseManifest
        {
            Schema = 1,
            Version = "2.27.6",
            Platform = "linux-x64",
            Channel = "stable",
            PublishedAt = DateTimeOffset.UtcNow,
            Notes = "Novità\n• punto",
            NotesMarkdown = "## Novità\n- **punto**",
            Files =
            [
                new ReleaseFile
                {
                    Path = "amz-cli",
                    Mode = "0755",
                    Size = 1,
                    Sha256 = new string('a', 64),
                    DownloadPath = "stable/linux-x64/2.27.6/amazon.gz",
                    DownloadSize = 1,
                    DownloadSha256 = new string('b', 64),
                    Compression = "gzip",
                },
            ],
        };
        var check = new UpdateCheckResult(
            UpdateStatus.UpdateAvailable,
            "2.27.5",
            "linux-x64",
            "stable",
            null,
            manifest,
            "Aggiornamento disponibile");
        var settings = new UpdateSettings { Width = 100 };

        UpdateRenderer.RenderCheck(check, settings, dryRun: false);
    }
    [Fact]
    public void Renders_headings_bullets_and_inline_styles()
    {
        var markup = MarkdownConsoleRenderer.ToMarkup(
            "## Titolo\n- **bold** e *italic*\n- [link](https://example.com)\n");

        Assert.Contains("[bold]", markup);
        Assert.Contains("• ", markup);
        Assert.Contains("[italic]", markup);
        Assert.Contains("[link=https://example.com]", markup);
        Assert.DoesNotContain("**bold**", markup);
    }

    [Fact]
    public void Escapes_square_brackets_from_plain_text()
    {
        var markup = MarkdownConsoleRenderer.ToMarkup("test [non markup]");

        Assert.Contains("[[non markup]]", markup);
    }

    [Fact]
    public void Renders_code_fences_as_grey_text()
    {
        var markup = MarkdownConsoleRenderer.ToMarkup(
            "```\n<tag> [x]\n```\n");

        Assert.Contains("[grey]", markup);
        Assert.Contains("[[x]]", markup);
    }

    [Fact]
    public void Malformed_markdown_never_throws()
    {
        var markup = MarkdownConsoleRenderer.ToMarkup(
            "**unclosed [link](https://example.com e *italic");

        Assert.False(string.IsNullOrWhiteSpace(markup));
    }
}
