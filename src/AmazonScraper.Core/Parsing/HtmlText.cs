using System.Net;
using System.Text.RegularExpressions;

namespace AmazonScraper.Core.Parsing;

internal static partial class HtmlText
{
    public static string Clean(string? fragment)
    {
        if (string.IsNullOrEmpty(fragment))
        {
            return string.Empty;
        }

        var withoutEmbedded = RemoveEmbeddedContent(fragment);
        var withoutTags = TagRegex().Replace(withoutEmbedded, string.Empty);
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    /// <summary>
    /// Removes comments, scripts and styles before HTML is flattened to text.
    /// Amazon embeds JavaScript inside otherwise ordinary detail cells (for
    /// example the rating summary row); without this step the script body is
    /// treated as visible text.
    /// </summary>
    internal static string RemoveEmbeddedContent(string fragment)
    {
        var withoutComments = CommentRegex().Replace(fragment, " ");
        withoutComments = ScriptRegex().Replace(withoutComments, " ");
        return StyleRegex().Replace(withoutComments, " ");
    }

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"<!--[\s\S]*?-->", RegexOptions.Singleline)]
    private static partial Regex CommentRegex();

    [GeneratedRegex(
        @"<script\b[^>]*>[\s\S]*?</script\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptRegex();

    [GeneratedRegex(
        @"<style\b[^>]*>[\s\S]*?</style\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StyleRegex();
}
