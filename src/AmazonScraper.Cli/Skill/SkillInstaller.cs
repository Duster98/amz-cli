using System.Text;

namespace AmazonScraper.Cli.Skill;

public enum SkillInstallStatus
{
    Missing,
    UpToDate,
    Outdated,
    Installed,
    Updated,
    Conflict,
}

public sealed record SkillInstallResult(
    string Path,
    SkillInstallStatus Status,
    string Message)
{
    public bool Success => Status is
        SkillInstallStatus.UpToDate
        or SkillInstallStatus.Installed
        or SkillInstallStatus.Updated;
}

/// <summary>
/// Installs the embedded skill in the conventional per-user agent directory.
/// The canonical file is written as UTF-8 without BOM; a custom file is not
/// overwritten unless --force is used.
/// </summary>
public static class SkillInstaller
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static string DefaultDirectory()
    {
        var home = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
        }
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Path.GetTempPath();
        }

        return Path.Combine(home, ".agents", "skills", "amz-cli");
    }

    public static string ResolvePath(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return Path.Combine(DefaultDirectory(), "SKILL.md");
        }

        var full = Path.GetFullPath(directory.Trim());
        return full.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? full
            : Path.Combine(full, "SKILL.md");
    }

    public static SkillInstallResult Check(string? directory = null)
    {
        var path = ResolvePath(directory);
        if (!File.Exists(path))
        {
            return new SkillInstallResult(
                path,
                SkillInstallStatus.Missing,
                $"Skill not installed: {path}");
        }

        var existing = ReadText(path);
        return IsCanonical(existing)
            ? new SkillInstallResult(
                path,
                SkillInstallStatus.UpToDate,
                $"Skill already up to date: {path}")
            : new SkillInstallResult(
                path,
                SkillInstallStatus.Outdated,
                $"Skill not up to date: {path}. Run 'amz-cli skill --install'.");
    }

    public static SkillInstallResult Install(
        string? directory = null,
        bool force = false)
    {
        var path = ResolvePath(directory);
        var existed = File.Exists(path);
        var existing = existed ? ReadText(path) : null;

        if (existing is not null && IsCanonical(existing))
        {
            return new SkillInstallResult(
                path,
                SkillInstallStatus.UpToDate,
                $"Skill already up to date: {path}");
        }

        if (existing is not null
            && !force
            && !LooksLikeAmazonSkill(existing))
        {
            return new SkillInstallResult(
                path,
                SkillInstallStatus.Conflict,
                $"A different file already exists: {path}. " +
                "Use 'amz-cli skill --install --force' to replace it.");
        }

        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(
                temporary,
                SkillDocument.Markdown,
                Utf8NoBom);
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception)
            {
                // Best effort.
            }

            throw;
        }

        return existed
            ? new SkillInstallResult(
                path,
                SkillInstallStatus.Updated,
                $"Skill updated: {path}")
            : new SkillInstallResult(
                path,
                SkillInstallStatus.Installed,
                $"Skill installed: {path}");
    }

    private static string ReadText(string path)
    {
        // File.ReadAllText detects a BOM; the canonical comparison normalizes
        // CRLF so a Windows editor does not make the skill look outdated.
        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static bool IsCanonical(string text) =>
        Normalize(text) == Normalize(SkillDocument.Markdown);

    private static string Normalize(string text) =>
        text.TrimStart('\uFEFF')
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);

    private static bool LooksLikeAmazonSkill(string text) =>
        text.Contains("name: amz-cli", StringComparison.Ordinal);
}
