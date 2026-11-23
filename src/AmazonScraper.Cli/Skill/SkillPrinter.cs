using System.Text;

namespace AmazonScraper.Cli.Skill;

public static class SkillPrinter
{
    public static void Print()
    {
        var markdown = SkillDocument.Markdown;

        // Write UTF-8 explicitly so Windows redirected stdout does not use the
        // console code page and break accents/Japanese characters.
        using var stdout = Console.OpenStandardOutput();
        using var writer = new StreamWriter(
            stdout,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        writer.Write(markdown);
        if (!markdown.EndsWith('\n'))
        {
            writer.WriteLine();
        }

        writer.Flush();
    }
}
