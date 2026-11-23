namespace AmazonScraper.Update.Tests;

public class PathSafetyTests
{
    [Theory]
    [InlineData("amz-cli")]
    [InlineData("amz-cli.exe")]
    [InlineData("libcurl-impersonate-licenses/LICENSE")]
    public void Accepts_safe_relative_paths(string path)
    {
        Assert.True(PathSafety.IsSafeRelativePath(path));
    }

    [Theory]
    [InlineData("../amz-cli")]
    [InlineData("folder/../../amz-cli")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\system32\\cmd.exe")]
    [InlineData("folder\\amz-cli")]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("folder//amz-cli")]
    public void Rejects_unsafe_paths(string path)
    {
        Assert.False(PathSafety.IsSafeRelativePath(path));
    }

    [Fact]
    public void CombineSafe_stays_below_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "amazon-update-test");

        var result = PathSafety.CombineSafe(root, "folder/amz-cli");

        Assert.StartsWith(Path.GetFullPath(root), result, StringComparison.Ordinal);
    }

    [Fact]
    public void CombineSafe_rejects_traversal()
    {
        Assert.Throws<UpdateException>(() =>
            PathSafety.CombineSafe("/tmp/install", "../outside"));
    }
}
