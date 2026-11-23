using AmazonScraper.Cli.EnvSetup;

namespace AmazonScraper.Cli.Tests;

public class PathListTests
{
    [Fact]
    public void Adds_to_an_empty_path()
    {
        var path = PathList.Add(string.Empty, @"C:\AmzCLI", out var added);

        Assert.True(added);
        Assert.Equal(@"C:\AmzCLI", path);
    }

    [Fact]
    public void Adds_without_duplicating_and_preserves_existing_entries()
    {
        var path = PathList.Add(
            @"C:\Windows;C:\Tools",
            @"C:\AmzCLI",
            out var added);

        Assert.True(added);
        Assert.Equal(@"C:\Windows;C:\Tools;C:\AmzCLI", path);
    }

    [Fact]
    public void Duplicate_is_case_insensitive_and_ignores_trailing_slash()
    {
        var path = PathList.Add(
            @"C:\AmzCLI\;C:\Tools",
            @"c:\amzcli",
            out var added);

        Assert.False(added);
        Assert.Equal(@"C:\AmzCLI\;C:\Tools", path);
    }

    [Fact]
    public void Add_handles_trailing_semicolon_and_spaces()
    {
        var path = PathList.Add(
            @"C:\Windows; ",
            @"C:\AmzCLI",
            out var added);

        Assert.True(added);
        Assert.Equal(@"C:\Windows;C:\AmzCLI", path);
    }

    [Fact]
    public void Contains_handles_quotes_and_case()
    {
        Assert.True(PathList.Contains(
            @"""C:\Program Files\AmzCLI"";C:\Windows",
            @"c:\program files\amzcli"));
        Assert.False(PathList.Contains(@"C:\Windows", @"C:\AmzCLI"));
    }

    [Fact]
    public void Remove_removes_only_the_matching_entry()
    {
        var path = PathList.Remove(
            @"C:\AmzCLI;C:\Windows;C:\Tools",
            @"c:\amzcli\",
            out var removed);

        Assert.True(removed);
        Assert.Equal(@"C:\Windows;C:\Tools", path);
    }

    [Fact]
    public void Remove_missing_entry_reports_no_change()
    {
        var path = PathList.Remove(
            @"C:\Windows;C:\Tools",
            @"C:\AmzCLI",
            out var removed);

        Assert.False(removed);
        Assert.Equal(@"C:\Windows;C:\Tools", path);
    }
}
