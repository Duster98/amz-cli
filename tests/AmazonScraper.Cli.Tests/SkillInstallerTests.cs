using System.Text;
using AmazonScraper.Cli.Skill;

namespace AmazonScraper.Cli.Tests;

public class SkillInstallerTests
{
    [Fact]
    public void Install_creates_the_skill_without_bom()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Root, "amz-cli", "SKILL.md");

        var result = SkillInstaller.Install(Path.GetDirectoryName(path));

        Assert.True(result.Success);
        Assert.Equal(SkillInstallStatus.Installed, result.Status);
        Assert.True(File.Exists(path));
        var bytes = File.ReadAllBytes(path);
        Assert.False(
            bytes.Length >= 3
            && bytes[0] == 0xEF
            && bytes[1] == 0xBB
            && bytes[2] == 0xBF);
        Assert.Equal(SkillDocument.Markdown, File.ReadAllText(path));
    }

    [Fact]
    public void Check_reports_missing_up_to_date_and_outdated()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Root, "amz-cli", "SKILL.md");

        Assert.Equal(
            SkillInstallStatus.Missing,
            SkillInstaller.Check(Path.GetDirectoryName(path)).Status);

        SkillInstaller.Install(Path.GetDirectoryName(path));
        Assert.Equal(
            SkillInstallStatus.UpToDate,
            SkillInstaller.Check(Path.GetDirectoryName(path)).Status);

        File.WriteAllText(path, "custom");
        Assert.Equal(
            SkillInstallStatus.Outdated,
            SkillInstaller.Check(Path.GetDirectoryName(path)).Status);
    }

    [Fact]
    public void Install_updates_an_existing_amazon_skill_without_force()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Root, "amz-cli", "SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            "---\nname: amz-cli\n---\n\nold skill\n",
            new UTF8Encoding(false));

        var result = SkillInstaller.Install(Path.GetDirectoryName(path));

        Assert.Equal(SkillInstallStatus.Updated, result.Status);
        Assert.Equal(SkillDocument.Markdown, File.ReadAllText(path));
    }

    [Fact]
    public void Install_does_not_overwrite_a_custom_file_without_force()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Root, "amz-cli", "SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "custom content");

        var result = SkillInstaller.Install(Path.GetDirectoryName(path));

        Assert.Equal(SkillInstallStatus.Conflict, result.Status);
        Assert.False(result.Success);
        Assert.Equal("custom content", File.ReadAllText(path));

        var forced = SkillInstaller.Install(
            Path.GetDirectoryName(path),
            force: true);
        Assert.Equal(SkillInstallStatus.Updated, forced.Status);
        Assert.Equal(SkillDocument.Markdown, File.ReadAllText(path));
    }

    [Fact]
    public void ResolvePath_accepts_a_directory_or_a_markdown_file()
    {
        using var directory = new TempDirectory();
        var file = Path.Combine(directory.Root, "SKILL.md");

        Assert.Equal(
            Path.Combine(directory.Root, "SKILL.md"),
            SkillInstaller.ResolvePath(directory.Root));
        Assert.Equal(file, SkillInstaller.ResolvePath(file));
    }
}
