using System.Reflection;
using System.Text;

namespace AmazonScraper.Cli.Skill;

public static class SkillDocument
{
    private const string ResourceName = "AmazonScraper.Cli.Skill.SKILL.md";

    public static string Markdown { get; } = Load();

    private static string Load()
    {
        using var stream = typeof(SkillDocument).Assembly
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Skill resource not found: {ResourceName}.");

        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);

        return reader.ReadToEnd();
    }
}
