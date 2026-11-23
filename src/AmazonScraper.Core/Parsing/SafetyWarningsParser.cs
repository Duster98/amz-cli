using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AmazonScraper.Core.Parsing;

/// <summary>
/// Extracts product safety warnings from the CPSIA card and from Amazon's
/// product quick view ("important information" and description). The same
/// notice is often rendered more than once; this parser keeps the longest
/// distinct version of each hazard.
/// </summary>
internal static partial class SafetyWarningsParser
{
    private static readonly string[] WarningMarkers =
    [
        "non adatto",
        "avvertenza",
        "attenzione",
        "rischio",
        "soffocamento",
        "piccole parti",
        "parti piccole",
        "piccolo magnete",
        "piccoli magneti",
        "sorveglianza",
        "protezione",
        "warning",
        "caution",
        "choking",
        "small parts",
        "small magnet",
        "small magnets",
        "not suitable",
        "avertissement",
        "advertencia",
        "warnung",
        "erstick",
        "kleine teile",
        "kleine magnete",
        "kleinen magnet",
        "no apto",
        "piezas pequeñas",
        "peligro de asfixia",
        "imanes pequeños",
        "imán pequeño",
        "petits aimants",
        "petit aimant",
        "swallow",
        "swallowed",
        "verschluck",
        "avaler",
        "ingerir",
        "ingerido",
        "ingerit",
        "supervision",
        "vigilancia",
        "vigilance",
        "aufsicht",
        "does not provide protection",
        "not provide protection",
        "no ofrece protección",
        "no proporciona protección",
        "keinen schutz",
        "ne protège pas",
        "保護しません",
        "not to be used",
        "no debe utilizarse",
        "ne doit pas être utilisé",
        "nicht verwendet werden",
        "over 36",
        "over three",
        "mayores de 36",
        "plus de 36",
        "über 36",
        "superiore a 36",
        "maggiore di 36",
        "36か月以上",
        "安全",
        "警告",
        "窒息",
        "小さな部品",
        "小さな磁石",
        "小さなマグネット",
        "飲み込",
    ];

    private static readonly HashSet<string> ExcludedHeadings = new(
        StringComparer.Ordinal)
    {
        "safety warning",
        "safety warnings",
        "avviso di sicurezza",
        "avvisi di sicurezza",
        "warnhinweis",
        "warnhinweise",
        "sicherheitshinweis",
        "sicherheitshinweise",
        "avertissement de securite",
        "avertissement de surete",
        "avertissements de securite",
        "advertencia de seguridad",
        "advertencias de seguridad",
        "not suitable for under 36 months",
        "not suitable under 36 months",
        "安全警告",
        "安全上のご注意",
    };

    private static readonly string[] LabelsToStrip =
    [
        "avvertenza ",
        "attenzione ",
        "warning ",
        "warnung ",
        "avertissement ",
        "advertencia ",
        "avviso ",
    ];

    public static List<string> Parse(string html)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(html))
        {
            return warnings;
        }

        // The CPSIA card is language-independent and carries the main hazard.
        foreach (Match card in SafetyCardRegex().Matches(html))
        {
            var content = AlertContentRegex().Match(card.Groups["body"].Value);
            if (content.Success)
            {
                Add(warnings, content.Groups["text"].Value);
            }
        }

        AddSection(warnings, ImportantSectionRegex(), html);
        AddSection(warnings, DescriptionSectionRegex(), html);

        return warnings;
    }

    private static void AddSection(
        List<string> warnings,
        Regex sectionRegex,
        string html)
    {
        var section = sectionRegex.Match(html);
        if (!section.Success)
        {
            return;
        }

        var content = section.Groups["section"].Value;
        foreach (Match item in ListItemRegex().Matches(content))
        {
            AddIfWarning(warnings, item.Groups["text"].Value);
        }
        foreach (Match paragraph in ParagraphRegex().Matches(content))
        {
            var inner = paragraph.Groups["text"].Value;
            if (inner.Contains("<ol", StringComparison.OrdinalIgnoreCase)
                || inner.Contains("<ul", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            AddIfWarning(warnings, inner);
        }
        foreach (Match div in LeafDivRegex().Matches(content))
        {
            AddIfWarning(warnings, div.Groups["text"].Value);
        }
    }

    private static void AddIfWarning(List<string> warnings, string? raw)
    {
        var text = HtmlText.Clean(raw);
        if (text.Length < 10
            || text.Contains("not_suitable_under_3_years", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var normalized = Normalize(text);
        if (normalized.Length == 0
            || ExcludedHeadings.Contains(normalized)
            || normalized.StartsWith("eta consigliata", StringComparison.Ordinal)
            || normalized.StartsWith("eta minima", StringComparison.Ordinal)
            || normalized.StartsWith("recommended age", StringComparison.Ordinal)
            || normalized.StartsWith("manufacturer minimum", StringComparison.Ordinal)
            || normalized.StartsWith("empfohlenes alter", StringComparison.Ordinal)
            || normalized.StartsWith("age recommande", StringComparison.Ordinal)
            || normalized.StartsWith("edad recomendada", StringComparison.Ordinal)
            || normalized.StartsWith("推奨年齢", StringComparison.Ordinal)
            || normalized.StartsWith("メーカー最小年齢", StringComparison.Ordinal))
        {
            return;
        }

        if (!WarningMarkers.Any(marker =>
                normalized.Contains(Normalize(marker), StringComparison.Ordinal)))
        {
            return;
        }

        Add(warnings, text);
    }

    private static void Add(List<string> warnings, string? raw)
    {
        var text = HtmlText.Clean(raw);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var normalized = Normalize(text);
        if (normalized.Length == 0)
        {
            return;
        }

        for (var index = 0; index < warnings.Count; index++)
        {
            var existing = Normalize(warnings[index]);
            if (SameHazard(existing, normalized))
            {
                if (normalized.Length > existing.Length)
                {
                    warnings[index] = text;
                }
                return;
            }
            if (existing.Contains(normalized, StringComparison.Ordinal))
            {
                return;
            }
            if (normalized.Contains(existing, StringComparison.Ordinal))
            {
                warnings[index] = text;
                return;
            }
        }

        warnings.Add(text);
    }

    private static bool SameHazard(string left, string right)
    {
        if (HasMagnetHazard(left) && HasMagnetHazard(right))
        {
            return true;
        }
        if (HasChokingHazard(left) && HasChokingHazard(right))
        {
            return true;
        }
        if (IsAgeRestriction(left) && IsAgeRestriction(right)
            && !HasChokingHazard(left) && !HasChokingHazard(right)
            && !HasMagnetHazard(left) && !HasMagnetHazard(right))
        {
            if (HasAny(left, Under36Markers) && HasAny(right, Under36Markers))
            {
                return true;
            }
            if (HasAny(left, Under3Markers) && HasAny(right, Under3Markers))
            {
                return true;
            }
            if (HasAny(left, Over36Markers) && HasAny(right, Over36Markers))
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasMagnetHazard(string value) =>
        (value.Contains("magnet", StringComparison.Ordinal)
            || value.Contains("iman", StringComparison.Ordinal)
            || value.Contains("aimant", StringComparison.Ordinal))
        && (value.Contains("inger", StringComparison.Ordinal)
            || value.Contains("swallow", StringComparison.Ordinal)
            || value.Contains("verschluck", StringComparison.Ordinal)
            || value.Contains("avaler", StringComparison.Ordinal)
            || value.Contains("tragan", StringComparison.Ordinal)
            || value.Contains("trago", StringComparison.Ordinal)
            || value.Contains("飲み込", StringComparison.Ordinal));

    private static bool HasChokingHazard(string value) =>
        HasAny(
            value,
            [
                "soffoc",
                "chok",
                "erstick",
                "etouff",
                "asfix",
                "窒息",
            ]);

    private static bool IsAgeRestriction(string value) =>
        HasAny(
            value,
            [
                "not suitable",
                "non adatto",
                "no apto",
                "no conviene",
                "not to be used",
                "no debe utilizarse",
                "nicht geeignet",
                "ne convient",
                "ne doit pas",
                "not for children",
                "non destinato",
                "do not use",
                "禁止",
                "対象外",
            ]);

    private static bool HasAny(string value, IReadOnlyList<string> markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.Ordinal));

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousSpace = true;
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character)
                == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousSpace = false;
            }
            else if (!previousSpace)
            {
                builder.Append(' ');
                previousSpace = true;
            }
        }

        var normalized = builder.ToString().Trim();
        foreach (var prefix in LabelsToStrip)
        {
            if (normalized.StartsWith(prefix, StringComparison.Ordinal))
            {
                normalized = normalized[prefix.Length..];
                break;
            }
        }

        return normalized;
    }

    private static readonly string[] Under36Markers =
    [
        "under 36",
        "inferiore a 36",
        "minori di 36",
        "menos de 36",
        "moins de 36",
        "unter 36",
        "36か月未満",
    ];

    private static readonly string[] Under3Markers =
    [
        "under 3 years",
        "under three",
        "sotto i 3",
        "inferiore a 3",
        "minori di 3",
        "menos de 3",
        "menores de 3",
        "moins de 3",
        "unter 3",
        "unter drei",
        "3歳未満",
    ];

    private static readonly string[] Over36Markers =
    [
        "over 36",
        "over three",
        "oltre i 36",
        "superiore a 36",
        "maggiore di 36",
        "mayores de 36",
        "plus de 36",
        "über 36",
        "ueber 36",
        "36か月以上",
    ];

    [GeneratedRegex(
        @"(?is)(?:data-card-metrics-id|cel_widget_id)=""[^""]*(?:cpsia|product-safety)[^""]*""(?<body>.{0,4000})")]
    private static partial Regex SafetyCardRegex();

    [GeneratedRegex(
        @"(?is)<div\b[^>]*class=""[^""]*a-alert-content[^""]*""[^>]*>(?<text>.*?)</div>")]
    private static partial Regex AlertContentRegex();

    [GeneratedRegex(
        @"(?is)<div\b[^>]*id=""pqv-important-information""[^>]*>(?<section>.*?)(?=<div\b[^>]*id=""pqv-|$)")]
    private static partial Regex ImportantSectionRegex();

    [GeneratedRegex(
        @"(?is)<div\b[^>]*id=""pqv-description""[^>]*>(?<section>.*?)(?=<div\b[^>]*id=""pqv-|$)")]
    private static partial Regex DescriptionSectionRegex();

    [GeneratedRegex(@"(?is)<li\b[^>]*>(?<text>.*?)</li>")]
    private static partial Regex ListItemRegex();

    [GeneratedRegex(@"(?is)<p\b[^>]*>(?<text>.*?)</p>")]
    private static partial Regex ParagraphRegex();

    [GeneratedRegex(@"(?is)<div\b[^>]*>(?<text>(?:(?!<div\b).)*?)</div>")]
    private static partial Regex LeafDivRegex();
}
