using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Models;

namespace AmazonScraper.Core.Parsing;

/// <summary>
/// Parses the rating summary and the "top reviews" that Amazon renders on the
/// product detail page. The separate product-reviews page requires an
/// authenticated session, so this parser deliberately works on the detail HTML
/// and never performs additional requests.
/// </summary>
public static partial class CustomerReviewsParser
{
    private static readonly string[] BodyNoise =
    [
        "Brief content visible, double tap to read full content.",
        "Full content visible, double tap to read brief content.",
        "Visualizza altro",
        "Visualizza meno",
        "Leggi di più",
        "Mostra meno",
        "See more",
        "See less",
        "Read more",
        "Read less",
        "Mehr anzeigen",
        "Weniger anzeigen",
        "Ver más",
        "Ver menos",
        "Lire la suite",
        "Afficher moins",
        "続きを読む",
        "表示を減らす",
    ];

    public static RatingDetails? ParseRating(
        string html,
        IMarketplaceProfile marketplace)
    {
        var average = ParseAverage(html, marketplace);
        var count = ParseCount(html);
        var histogram = ParseHistogram(html);

        if (average is null && count is null && histogram.Count == 0)
        {
            return null;
        }

        return new RatingDetails
        {
            Average = average,
            Count = count,
            Histogram = histogram,
        };
    }

    public static TopReviews? ParseReviews(
        string html,
        IMarketplaceProfile marketplace,
        int? limit = null,
        bool includeImages = false)
    {
        var items = new List<ProductReview>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        AddList("localTopReviewsList", "local");
        AddList("internationalTopReviewsList", "international");

        if (items.Count == 0)
        {
            AddList("cm-cr-dp-review-list", "local");
            AddList("cm-cr-global-review-list", "international");
        }

        if (items.Count == 0)
        {
            foreach (var block in ExtractReviewBlocks(html))
            {
                Add(block, "local");
            }
        }

        if (limit is > 0 && items.Count > limit.Value)
        {
            items = items.Take(limit.Value).ToList();
        }

        return items.Count == 0 ? null : new TopReviews { Items = items };

        void AddList(string id, string source)
        {
            var section = ExtractElementById(html, id);
            if (section is null)
            {
                return;
            }

            foreach (var block in ExtractReviewBlocks(section))
            {
                Add(block, source);
            }
        }

        void Add(ReviewBlock block, string source)
        {
            var review = ParseReview(block, source, marketplace, includeImages);
            if (!string.IsNullOrEmpty(review.Id) && !seen.Add(review.Id))
            {
                return;
            }

            items.Add(review);
        }
    }

    private static double? ParseAverage(
        string html,
        IMarketplaceProfile marketplace)
    {
        var title = AttributeById(html, "acrPopover", "title");
        if (string.IsNullOrEmpty(title))
        {
            title = HtmlText.Clean(ExtractElementByDataHook(html, "rating-out-of-text"));
        }

        var fromSummary = ParseRatingText(title);
        if (fromSummary is not null)
        {
            return fromSummary;
        }

        var match = marketplace.Patterns.DetailRating.Match(html);
        if (!match.Success)
        {
            match = marketplace.Patterns.DetailRatingFallback.Match(html);
        }

        return match.Success && TryParseRatingNumber(match.Groups[1].Value, out var value)
            ? value
            : null;
    }

    private static int? ParseCount(string html)
    {
        var text = AttributeById(html, "acrCustomerReviewText", "aria-label")
            ?? HtmlText.Clean(ExtractElementById(html, "acrCustomerReviewText"));
        if (string.IsNullOrWhiteSpace(text))
        {
            text = HtmlText.Clean(ExtractElementByDataHook(html, "total-review-count"));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = CountRegex().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var digits = new string(match.Value.Where(char.IsDigit).ToArray());
        return int.TryParse(
            digits,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var count)
            ? count
            : null;
    }

    private static Dictionary<string, int> ParseHistogram(string html)
    {
        var histogram = new Dictionary<string, int>(StringComparer.Ordinal);
        var section = ExtractElementById(html, "histogramTable")
            ?? ExtractElementByDataHook(html, "histogram");
        if (string.IsNullOrWhiteSpace(section))
        {
            return histogram;
        }

        foreach (Match row in HistogramRowRegex().Matches(section))
        {
            var key = row.Groups["star"].Value.ToLowerInvariant() switch
            {
                "five" => "5",
                "four" => "4",
                "three" => "3",
                "two" => "2",
                "one" => "1",
                _ => null,
            };
            if (key is null || histogram.ContainsKey(key))
            {
                continue;
            }

            var value = ParsePercent(row.Value);
            if (value is not null)
            {
                histogram[key] = value.Value;
            }
        }

        var ordered = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var key in new[] { "5", "4", "3", "2", "1" })
        {
            if (histogram.TryGetValue(key, out var value))
            {
                ordered[key] = value;
            }
            else if (histogram.Count > 0)
            {
                ordered[key] = 0;
            }
        }

        return ordered;
    }

    private static ProductReview ParseReview(
        ReviewBlock block,
        string source,
        IMarketplaceProfile marketplace,
        bool includeImages)
    {
        var id = AttributeValue(block.StartTag, "id")
            ?? AttributeByDataHook(block.Inner, "helpfulVoteButton", "data-id")
            ?? string.Empty;
        var author = HtmlText.Clean(ProfileNameRegex().Match(block.Inner) is { Success: true } authorMatch
            ? authorMatch.Groups[1].Value
            : string.Empty);
        var ratingValue = ParseStarValue(block.Inner);
        var title = StripStarPrefix(HtmlText.Clean(
            ExtractElementByDataHook(block.Inner, "reviewTitle", "review-title")));
        var date = HtmlText.Clean(
            ExtractElementByDataHook(block.Inner, "review-date"));
        var helpful = HtmlText.Clean(
            ExtractElementByDataHook(block.Inner, "helpful-vote-statement"));
        var variant = ExtractElementByDataHook(block.Inner, "product-variation-attributes");
        if (!string.IsNullOrEmpty(variant))
        {
            variant = RemoveElementByDataHook(variant, "avp-badge");
        }

        var body = CleanBody(ExtractElementByDataHook(
            block.Inner,
            "reviewRichContentContainer",
            "review-body",
            "reviewText"));

        var url = ExtractReviewUrl(block.Inner, marketplace);
        if (string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(id))
        {
            url = new Uri(
                marketplace.BaseUri,
                $"gp/customer-reviews/{Uri.EscapeDataString(id)}").AbsoluteUri;
        }

        return new ProductReview
        {
            Id = id,
            Author = author,
            Rating = ratingValue is null
                ? string.Empty
                : marketplace.Format.Rating(FormatRatingValue(ratingValue.Value)),
            RatingValue = ratingValue,
            Title = title,
            Date = date,
            Source = source,
            VerifiedPurchase = FindStartTagByDataHook(block.Inner, "avp-badge") is not null,
            Helpful = helpful,
            Variant = CleanBlock(variant),
            Body = body,
            Url = url,
            Images = includeImages ? ExtractImages(block.Inner) : null,
        };
    }

    private static double? ParseStarValue(string html)
    {
        var start = FindStartTagByDataHook(html, "review-star-rating")
            ?? FindStartTagByDataHook(html, "review-rating");
        if (start is null)
        {
            return null;
        }

        var value = ParseStarClass(AttributeValue(start.Value, "class"));
        if (value is not null)
        {
            return value;
        }

        var starHtml = ExtractBalanced(html, start).Inner;
        var alt = IconAltRegex().Match(starHtml);
        return alt.Success
            ? ParseRatingText(HtmlText.Clean(alt.Groups[1].Value))
            : null;
    }

    private static double? ParseStarClass(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var match = StarClassRegex().Match(value);
        if (!match.Success)
        {
            return null;
        }

        var whole = int.Parse(
            match.Groups["whole"].Value,
            NumberStyles.None,
            CultureInfo.InvariantCulture);
        var half = match.Groups["half"].Success
            && string.Equals(match.Groups["half"].Value, "5", StringComparison.Ordinal)
                ? 0.5
                : 0;
        return whole + half;
    }

    private static double? ParseRatingText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var numbers = NumberRegex().Matches(text)
            .Select(match => TryParseRatingNumber(match.Value, out var value)
                ? value
                : (double?)null)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToList();
        if (numbers.Count == 0)
        {
            return null;
        }

        // Japanese shows "5つ星のうち4.7": the first number is the scale.
        return text.Contains("つ星のうち", StringComparison.Ordinal)
            ? numbers[^1]
            : numbers[0];
    }

    private static bool TryParseRatingNumber(string? value, out double number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Replace(',', '.').Trim();
        return double.TryParse(
                normalized,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out number)
            && number is >= 0 and <= 5;
    }

    private static int? ParsePercent(string value)
    {
        var match = AriaValueNowRegex().Match(value);
        if (!match.Success)
        {
            match = AriaLabelPercentRegex().Match(value);
        }
        if (!match.Success)
        {
            match = WidthPercentRegex().Match(value);
        }

        return match.Success
            && int.TryParse(
                match.Groups["value"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var percent)
            && percent is >= 0 and <= 100
                ? percent
                : null;
    }

    private static string FormatRatingValue(double value) =>
        value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string StripStarPrefix(string title)
    {
        if (string.IsNullOrEmpty(title))
        {
            return string.Empty;
        }

        return StarPrefixRegex().Replace(title, string.Empty).Trim();
    }

    private static string CleanBody(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        html = HtmlText.RemoveEmbeddedContent(html);

        // Paragraphs and <br> are meaningful in reviews: preserve them as
        // newlines instead of flattening the whole body to a single line.
        var withLines = LineBreakRegex().Replace(html, "\n");
        withLines = BlockBoundaryRegex().Replace(withLines, "\n");
        withLines = BlockTagRegex().Replace(withLines, " ");
        var text = WebUtility.HtmlDecode(withLines);
        foreach (var noise in BodyNoise)
        {
            text = text.Replace(noise, " ", StringComparison.OrdinalIgnoreCase);
        }

        text = text.Replace("\uFFFD", " ", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        text = HorizontalWhitespaceRegex().Replace(text, " ");
        text = LinePaddingRegex().Replace(text, "\n");
        text = MultipleNewlinesRegex().Replace(text, "\n\n");
        text = SpaceBeforePunctuationRegex().Replace(text, "$1");
        return text.Trim();
    }

    private static string CleanBlock(string? fragment)
    {
        if (string.IsNullOrWhiteSpace(fragment))
        {
            return string.Empty;
        }

        var spaced = BlockTagRegex().Replace(fragment, " ");
        var text = HtmlText.Clean(spaced);
        return SpaceBeforePunctuationRegex().Replace(text, "$1").Trim();
    }

    private static string ExtractReviewUrl(
        string html,
        IMarketplaceProfile marketplace)
    {
        foreach (Match match in ReviewUrlRegex().Matches(html))
        {
            var href = WebUtility.HtmlDecode(match.Groups["url"].Value);
            if (href.Contains("/profile/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return Uri.TryCreate(marketplace.BaseUri, href, out var uri)
                ? uri.AbsoluteUri
                : href;
        }

        return string.Empty;
    }

    private static List<string> ExtractImages(string html)
    {
        var images = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in ImageTagRegex().Matches(html))
        {
            var attributes = match.Groups["attrs"].Value;
            if (!attributes.Contains("review-image-tile", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Add(AttributeValue(attributes, "data-src")
                ?? AttributeValue(attributes, "src"));
        }

        foreach (Match start in StartTagWithDataHookRegex().Matches(html))
        {
            if (!string.Equals(
                    start.Groups["hook"].Value,
                    "review-image-tile",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var tag = start.Groups["tag"].Value;
            if (string.Equals(tag, "img", StringComparison.OrdinalIgnoreCase)
                || start.Value.TrimEnd().EndsWith("/>", StringComparison.Ordinal))
            {
                continue;
            }

            var inner = ExtractBalanced(html, start).Inner;
            foreach (Match image in ImageTagRegex().Matches(inner))
            {
                Add(AttributeValue(image.Groups["attrs"].Value, "data-src")
                    ?? AttributeValue(image.Groups["attrs"].Value, "src"));
            }
        }

        return images;

        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value.Contains("grey-pixel", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var url = WebUtility.HtmlDecode(value);
            if (seen.Add(url))
            {
                images.Add(url);
            }
        }
    }

    private static List<ReviewBlock> ExtractReviewBlocks(string containerHtml)
    {
        var blocks = new List<ReviewBlock>();
        var index = 0;
        while (index < containerHtml.Length)
        {
            var match = ReviewStartTagRegex().Match(containerHtml, index);
            if (!match.Success)
            {
                break;
            }

            var (inner, end) = ExtractBalanced(containerHtml, match);
            blocks.Add(new ReviewBlock(match.Value, inner));
            index = end > match.Index ? end : match.Index + match.Length;
        }

        return blocks;
    }

    private static string? ExtractElementById(string html, string id)
    {
        foreach (Match match in StartTagWithIdRegex().Matches(html))
        {
            if (!string.Equals(match.Groups["id"].Value, id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return ExtractBalanced(html, match).Inner;
        }

        return null;
    }

    private static string? ExtractElementByDataHook(string html, params string[] hooks)
    {
        foreach (var hook in hooks)
        {
            var start = FindStartTagByDataHook(html, hook);
            if (start is not null)
            {
                return ExtractBalanced(html, start).Inner;
            }
        }

        return null;
    }

    private static Match? FindStartTagByDataHook(string html, string hook)
    {
        foreach (Match match in StartTagWithDataHookRegex().Matches(html))
        {
            if (string.Equals(
                    match.Groups["hook"].Value,
                    hook,
                    StringComparison.OrdinalIgnoreCase))
            {
                return match;
            }
        }

        return null;
    }

    private static string? AttributeById(string html, string id, string attribute)
    {
        foreach (Match match in StartTagWithIdRegex().Matches(html))
        {
            if (string.Equals(match.Groups["id"].Value, id, StringComparison.OrdinalIgnoreCase))
            {
                return AttributeValue(match.Value, attribute);
            }
        }

        return null;
    }

    private static string? AttributeByDataHook(string html, string hook, string attribute)
    {
        var match = FindStartTagByDataHook(html, hook);
        return match is null ? null : AttributeValue(match.Value, attribute);
    }

    private static string? AttributeValue(string tag, string attribute)
    {
        var match = Regex.Match(
            tag,
            $@"\b{Regex.Escape(attribute)}\s*=\s*[""'](?<value>[^""']*)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success
            ? WebUtility.HtmlDecode(match.Groups["value"].Value)
            : null;
    }

    private static string RemoveElementByDataHook(string html, string hook)
    {
        var start = FindStartTagByDataHook(html, hook);
        if (start is null)
        {
            return html;
        }

        var (_, end) = ExtractBalanced(html, start);
        return html[..start.Index] + html[end..];
    }

    private static (string Inner, int End) ExtractBalanced(string html, Match startMatch)
    {
        var tag = startMatch.Groups["tag"].Value;
        var contentStart = startMatch.Index + startMatch.Length;
        var depth = 1;
        var tagRegex = new Regex(
            $@"<(?<close>/)?{Regex.Escape(tag)}\b[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var match = tagRegex.Match(html, contentStart);
        while (match.Success)
        {
            var isClose = match.Groups["close"].Success;
            var selfClosing = match.Value.TrimEnd().EndsWith("/>", StringComparison.Ordinal);
            if (isClose)
            {
                depth--;
            }
            else if (!selfClosing)
            {
                depth++;
            }

            if (depth == 0)
            {
                return (
                    html[contentStart..match.Index],
                    match.Index + match.Length);
            }

            match = match.NextMatch();
        }

        return (html[contentStart..], html.Length);
    }

    private readonly record struct ReviewBlock(string StartTag, string Inner);

    [GeneratedRegex(@"<(?<tag>[a-zA-Z0-9]+)\b[^>]*\bid\s*=\s*[""'](?<id>[^""']+)[""'][^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StartTagWithIdRegex();

    [GeneratedRegex(@"<(?<tag>[a-zA-Z0-9]+)\b[^>]*\bdata-hook\s*=\s*[""'](?<hook>[^""']+)[""'][^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StartTagWithDataHookRegex();

    [GeneratedRegex(@"<(?<tag>[a-zA-Z0-9]+)\b[^>]*\bdata-hook\s*=\s*[""']review[""'][^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ReviewStartTagRegex();

    [GeneratedRegex(@"<a\b(?<attrs>[^>]*?)href\s*=\s*[""'][^""']*filterByStar=(?<star>five|four|three|two|one)_star[^""']*[""'][^>]*>(?<body>[\s\S]*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HistogramRowRegex();

    [GeneratedRegex(@"aria-valuenow\s*=\s*[""'](?<value>\d+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex AriaValueNowRegex();

    [GeneratedRegex(@"aria-label\s*=\s*[""'][^""']*(?<value>\d+)\s*%", RegexOptions.IgnoreCase)]
    private static partial Regex AriaLabelPercentRegex();

    [GeneratedRegex(@"width\s*:\s*(?<value>\d+)%", RegexOptions.IgnoreCase)]
    private static partial Regex WidthPercentRegex();

    [GeneratedRegex(@"\d+(?:[.,]\d+)?")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"\d[\d.,\s\u00A0\u202F]*")]
    private static partial Regex CountRegex();

    [GeneratedRegex(@"<span[^>]*class\s*=\s*[""'][^""']*\ba-profile-name\b[^""']*[""'][^>]*>(?<value>[\s\S]*?)</span>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ProfileNameRegex();

    [GeneratedRegex(@"<span[^>]*class\s*=\s*[""'][^""']*\ba-icon-alt\b[^""']*[""'][^>]*>(?<value>[\s\S]*?)</span>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex IconAltRegex();

    [GeneratedRegex(@"a-star-(?<whole>\d)(?:-(?<half>\d))?")]
    private static partial Regex StarClassRegex();

    [GeneratedRegex(@"^\s*(?:\d+(?:[.,]\d+)?\s*(?:out of 5 stars|su 5 stelle|von 5 sternen|de 5 estrellas|sur 5|5つ星のうち)|5つ星のうち\s*\d+(?:[.,]\d+)?)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex StarPrefixRegex();

    [GeneratedRegex(@"<a\b[^>]*href\s*=\s*[""'](?<url>[^""']*(?:customer-reviews|product-reviews)[^""']*)[""'][^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ReviewUrlRegex();

    [GeneratedRegex(@"<img\b(?<attrs>[^>]*)>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ImageTagRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex BlockTagRegex();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakRegex();

    [GeneratedRegex(
        @"</?(?:p|div|li|ul|ol|h[1-6]|blockquote|section|article)\b[^>]*>",
        RegexOptions.IgnoreCase)]
    private static partial Regex BlockBoundaryRegex();

    [GeneratedRegex(@"[^\S\n]+")]
    private static partial Regex HorizontalWhitespaceRegex();

    [GeneratedRegex(@"[^\S\n]*\n[^\S\n]*")]
    private static partial Regex LinePaddingRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultipleNewlinesRegex();

    [GeneratedRegex(@"\s+([,.;:!?])")]
    private static partial Regex SpaceBeforePunctuationRegex();
}
