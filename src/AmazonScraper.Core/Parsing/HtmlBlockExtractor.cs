namespace AmazonScraper.Core.Parsing;

using System.Text.RegularExpressions;

internal static class HtmlBlockExtractor
{
    /// <summary>
    /// Extracts the first &lt;div&gt; whose id matches <paramref name="id"/>,
    /// balancing nested div tags.
    /// </summary>
    public static string ExtractDivById(string html, string id)
    {
        var match = Regex.Match(
            html,
            $@"<div\b[^>]*\bid\s*=\s*[""']{Regex.Escape(id)}[""'][^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? ExtractDiv(html, match.Index) : string.Empty;
    }

    /// <summary>
    /// Python <c>_card_div</c>: starting from a position inside an opening
    /// tag, return the full outer &lt;div&gt; block or an empty string.
    /// </summary>
    public static string ExtractDiv(string html, int position)
    {
        var start = html.IndexOf('>', position) + 1;
        if (start <= 0)
        {
            return string.Empty;
        }

        var depth = 1;
        var i = start;

        while (i < html.Length && depth > 0)
        {
            var opening = html.IndexOf("<div", i, StringComparison.Ordinal);
            var closing = html.IndexOf("</div>", i, StringComparison.Ordinal);

            if (closing < 0)
            {
                break;
            }

            if (opening >= 0 && opening < closing)
            {
                depth++;
                var tagEnd = html.IndexOf('>', opening + 4);
                if (tagEnd < 0)
                {
                    return string.Empty;
                }
                i = tagEnd + 1;
            }
            else
            {
                depth--;
                i = closing + 6;
            }
        }

        return depth == 0 ? html[position..i] : string.Empty;
    }
}
