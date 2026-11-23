using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Searching;

/// <summary>
/// One department/category refinement exposed by Amazon's deals page.
/// </summary>
public sealed record DealCategory(string Label, string NodeId);

/// <summary>
/// Discovers the department refinements from the deals HTML. Amazon does not
/// expose a stable public catalog, so the node IDs are read from the page of
/// the selected marketplace instead of being hard-coded.
/// </summary>
public static partial class DealCategoryCatalog
{
    public static IReadOnlyList<DealCategory> Parse(string html)
    {
        var categories = new List<DealCategory>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in CategoryLinkRegex().Matches(html))
        {
            var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
            var idMatch = NodeIdRegex().Match(href);
            if (!idMatch.Success || !seen.Add(idMatch.Groups["id"].Value))
            {
                continue;
            }

            var label = HtmlText.Clean(match.Groups["label"].Value);
            if (!string.IsNullOrWhiteSpace(label))
            {
                categories.Add(new DealCategory(label, idMatch.Groups["id"].Value));
            }
        }

        return categories;
    }

    public static bool TryResolve(
        IReadOnlyList<DealCategory> categories,
        string query,
        out DealCategory category)
    {
        var normalizedQuery = Normalize(query);
        if (normalizedQuery.Length == 0)
        {
            category = null!;
            return false;
        }

        var exact = categories
            .Where(candidate => Normalize(candidate.Label) == normalizedQuery)
            .ToList();
        if (exact.Count == 1)
        {
            category = exact[0];
            return true;
        }

        var partial = categories
            .Where(candidate =>
            {
                var label = Normalize(candidate.Label);
                return label.Contains(normalizedQuery, StringComparison.Ordinal)
                    || normalizedQuery.Contains(label, StringComparison.Ordinal);
            })
            .OrderBy(candidate => candidate.Label.Length)
            .ToList();
        if (partial.Count > 0)
        {
            category = partial[0];
            return true;
        }

        category = null!;
        return false;
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(character)
                ? char.ToLowerInvariant(character)
                : ' ');
        }

        return WhitespaceRegex().Replace(builder.ToString(), " ").Trim();
    }

    [GeneratedRegex(@"<a\b[^>]*href\s*=\s*[""'](?<href>[^""']*n%3A\d+[^""']*p_n_deal_type[^""']*)[""'][^>]*>(?<label>[\s\S]*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CategoryLinkRegex();

    [GeneratedRegex(@"n%3A(?<id>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex NodeIdRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
