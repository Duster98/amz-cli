using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace AmazonScraper.Cli.Rendering;

/// <summary>
/// Minimal Markdown to Spectre markup renderer for release notes.
/// It supports headings, bullets, numbered lists, blockquotes, fenced code,
/// inline code, bold, italic, strikethrough and links. Unknown syntax is
/// rendered as plain text, never as raw Spectre markup.
/// </summary>
public static class MarkdownConsoleRenderer
{
    private static readonly Regex Heading = new(
        @"^(?<level>#{1,6})\s+(?<text>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex Bullet = new(
        @"^(?<indent>\s*)[-*+]\s+(?<text>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex Numbered = new(
        @"^(?<indent>\s*)(?<number>\d+)[.)]\s+(?<text>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex Quote = new(
        @"^\s*>\s?(?<text>.*)$",
        RegexOptions.Compiled);

    private static readonly Regex Rule = new(
        @"^\s*([-*_])(?:\s*\1){2,}\s*$",
        RegexOptions.Compiled);

    private static readonly Regex Inline = new(
        @"(?<bold>\*\*(?<boldText>.+?)\*\*)" +
        @"|(?<strike>~~(?<strikeText>.+?)~~)" +
        @"|(?<code>`(?<codeText>[^`]+)`)" +
        @"|(?<link>\[(?<linkText>[^\]]+)\]\((?<linkUrl>[^)]+)\))" +
        @"|(?<italic>\*(?<italicText>[^*]+)\*)",
        RegexOptions.Compiled);

    public static string ToMarkup(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        try
        {
            var output = new StringBuilder();
            var inCode = false;
            var code = new StringBuilder();
            var normalized = markdown
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');

            foreach (var rawLine in normalized.Split('\n'))
            {
                var line = rawLine.TrimEnd();

                if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    if (inCode)
                    {
                        FlushCode(output, code);
                    }
                    inCode = !inCode;
                    continue;
                }

                if (inCode)
                {
                    if (code.Length > 0)
                    {
                        code.Append('\n');
                    }
                    code.Append(line);
                    continue;
                }

                if (Rule.IsMatch(line))
                {
                    output.AppendLine("[grey]────────────────[/]");
                    continue;
                }

                var heading = Heading.Match(line);
                if (heading.Success)
                {
                    var level = heading.Groups["level"].Value.Length;
                    var text = InlineMarkup(heading.Groups["text"].Value);
                    output.Append("[bold");
                    if (level == 1)
                    {
                        output.Append(" cyan");
                    }
                    output.Append(']').Append(text).AppendLine("[/]");
                    continue;
                }

                var bullet = Bullet.Match(line);
                if (bullet.Success)
                {
                    output.Append(bullet.Groups["indent"].Value)
                        .Append("• ")
                        .AppendLine(InlineMarkup(bullet.Groups["text"].Value));
                    continue;
                }

                var numbered = Numbered.Match(line);
                if (numbered.Success)
                {
                    output.Append(numbered.Groups["indent"].Value)
                        .Append(numbered.Groups["number"].Value)
                        .Append(". ")
                        .AppendLine(InlineMarkup(numbered.Groups["text"].Value));
                    continue;
                }

                var quote = Quote.Match(line);
                if (quote.Success)
                {
                    output.Append("[grey]│[/] ")
                        .AppendLine(InlineMarkup(quote.Groups["text"].Value));
                    continue;
                }

                output.AppendLine(InlineMarkup(line));
            }

            if (inCode)
            {
                FlushCode(output, code);
            }

            return output.ToString().TrimEnd();
        }
        catch (Exception)
        {
            // A malformed Markdown note must never break the update check.
            return Markup.Escape(markdown);
        }
    }

    private static void FlushCode(StringBuilder output, StringBuilder code)
    {
        foreach (var line in code.ToString().Split('\n'))
        {
            output.Append("[grey]").Append(Markup.Escape(line)).AppendLine("[/]");
        }
        code.Clear();
    }

    private static string InlineMarkup(string text)
    {
        var output = new StringBuilder();
        var position = 0;
        foreach (Match match in Inline.Matches(text))
        {
            if (match.Index > position)
            {
                output.Append(Markup.Escape(text[position..match.Index]));
            }

            if (match.Groups["bold"].Success)
            {
                output.Append("[bold]")
                    .Append(Markup.Escape(match.Groups["boldText"].Value))
                    .Append("[/]");
            }
            else if (match.Groups["strike"].Success)
            {
                output.Append("[strikethrough]")
                    .Append(Markup.Escape(match.Groups["strikeText"].Value))
                    .Append("[/]");
            }
            else if (match.Groups["code"].Success)
            {
                output.Append("[grey]")
                    .Append(Markup.Escape(match.Groups["codeText"].Value))
                    .Append("[/]");
            }
            else if (match.Groups["link"].Success)
            {
                var label = match.Groups["linkText"].Value;
                var url = match.Groups["linkUrl"].Value;
                if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    output.Append("[link=")
                        .Append(Markup.Escape(url))
                        .Append(']')
                        .Append(Markup.Escape(label))
                        .Append("[/]");
                }
                else
                {
                    output.Append(Markup.Escape($"{label} ({url})"));
                }
            }
            else if (match.Groups["italic"].Success)
            {
                output.Append("[italic]")
                    .Append(Markup.Escape(match.Groups["italicText"].Value))
                    .Append("[/]");
            }

            position = match.Index + match.Length;
        }

        if (position < text.Length)
        {
            output.Append(Markup.Escape(text[position..]));
        }

        return output.ToString();
    }
}
