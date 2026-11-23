using System.Text.RegularExpressions;

namespace AmazonScraper.Core.Models;

public sealed record BuyingConditionInfo(
    string ConditionType,
    string ConditionGrade);

/// <summary>
/// Normalizes Amazon condition labels into stable tokens. The raw label is
/// still returned by <see cref="BuyingOption.Condition"/>; these tokens make
/// the condition type and the Renewed grade explicit for clients.
/// </summary>
public static class BuyingConditionClassifier
{
    private static readonly Regex Refurbished = new(
        "ricondizionat|rinnovato|refurb|renewed|remis|rigenerat|" +
        "reconditionn|generalüberholt|generalueberholt|reacondicionad|" +
        "整備済み",
        RegexOptions.Compiled | RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant);

    private static readonly Regex Used = new(
        @"\busat[oaie]\b|\bused\b|occasion|gebraucht|\busado\b|" +
        "segunda mano|second hand|中古",
        RegexOptions.Compiled | RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant);

    private static readonly Regex New = new(
        @"\bnuov[oaie]\b|\bnew\b|\bneu\b|\bneuf\b|\bnuevo\b|新品",
        RegexOptions.Compiled | RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant);

    private static readonly Regex PremiumGrade = new(
        @"\bpremium\b|プレミアム",
        RegexOptions.Compiled | RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant);

    private static readonly Regex ExcellentGrade = new(
        "eccellent|excellent|sehr gut|sehr gute|tr[eè]s bon|muy bueno|" +
        "very good|ottimo|come nuovo|like new|非常に良い|极佳|極上",
        RegexOptions.Compiled | RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant);

    private static readonly Regex GoodGrade = new(
        @"\bbuono\b|\bgood\b|\bgut(?:e)?\b|\bbon\b|\bbueno\b|良い",
        RegexOptions.Compiled | RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant);

    private static readonly Regex AcceptableGrade = new(
        "accettabile|acceptable|akzeptabel|aceptable|許容|可",
        RegexOptions.Compiled | RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant);

    public static BuyingConditionInfo Parse(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return new BuyingConditionInfo(string.Empty, string.Empty);
        }

        var type = Refurbished.IsMatch(text)
            ? "refurbished"
            : Used.IsMatch(text)
                ? "used"
                : New.IsMatch(text)
                    ? "new"
                    : string.Empty;
        var grade = PremiumGrade.IsMatch(text)
            ? "premium"
            : ExcellentGrade.IsMatch(text)
                ? "excellent"
                : GoodGrade.IsMatch(text)
                    ? "good"
                    : AcceptableGrade.IsMatch(text)
                        ? "acceptable"
                        : string.Empty;
        return new BuyingConditionInfo(type, grade);
    }
}
