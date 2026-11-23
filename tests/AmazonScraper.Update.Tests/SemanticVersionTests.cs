namespace AmazonScraper.Update.Tests;

public class SemanticVersionTests
{
    [Theory]
    [InlineData("1.0.0", 1, 0, 0)]
    [InlineData("2.25.0", 2, 25, 0)]
    [InlineData("2.25.0-beta.1+build.5", 2, 25, 0)]
    public void Parses_valid_versions(string text, int major, int minor, int patch)
    {
        var version = SemanticVersion.Parse(text);

        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("01.0.0")]
    [InlineData("1.0.0.0")]
    [InlineData("v1.0.0")]
    [InlineData("")]
    public void Rejects_invalid_versions(string text)
    {
        Assert.False(SemanticVersion.TryParse(text, out _));
    }

    [Fact]
    public void Release_is_greater_than_prerelease()
    {
        var release = SemanticVersion.Parse("1.0.0");
        var prerelease = SemanticVersion.Parse("1.0.0-beta.1");

        Assert.True(release.CompareTo(prerelease) > 0);
        Assert.True(prerelease.CompareTo(release) < 0);
    }

    [Fact]
    public void Prerelease_identifiers_follow_semver_order()
    {
        var alpha = SemanticVersion.Parse("1.0.0-alpha");
        var alpha1 = SemanticVersion.Parse("1.0.0-alpha.1");
        var alphaBeta = SemanticVersion.Parse("1.0.0-alpha.beta");
        var beta = SemanticVersion.Parse("1.0.0-beta");
        var beta2 = SemanticVersion.Parse("1.0.0-beta.2");
        var beta11 = SemanticVersion.Parse("1.0.0-beta.11");
        var rc = SemanticVersion.Parse("1.0.0-rc.1");

        Assert.True(alpha.CompareTo(alpha1) < 0);
        Assert.True(alpha1.CompareTo(alphaBeta) < 0);
        Assert.True(alphaBeta.CompareTo(beta) < 0);
        Assert.True(beta.CompareTo(beta2) < 0);
        Assert.True(beta2.CompareTo(beta11) < 0);
        Assert.True(beta11.CompareTo(rc) < 0);
    }

    [Fact]
    public void Build_metadata_is_ignored_for_precedence()
    {
        var left = SemanticVersion.Parse("1.2.3+one");
        var right = SemanticVersion.Parse("1.2.3+two");

        Assert.Equal(0, left.CompareTo(right));
    }
}
