using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class DealEndDateParserTests
{
    [Theory]
    [InlineData("Questa offerta termina il 30 novembre 2026", "2026-11-30")]
    [InlineData("Dieses Angebot endet am 30. November 2026", "2026-11-30")]
    [InlineData("Cette offre se termine le 30 novembre 2026", "2026-11-30")]
    [InlineData("Esta oferta termina el 30 de noviembre de 2026", "2026-11-30")]
    [InlineData("This deal ends on 30 November 2026", "2026-11-30")]
    [InlineData("This deal ends on November 30, 2026", "2026-11-30")]
    [InlineData("This deal ends on Nov. 30, 2026", "2026-11-30")]
    [InlineData("このオファーは2026年11月30日に終了します", "2026-11-30")]
    [InlineData("Offerta valida fino al 2026-11-30", "2026-11-30")]
    [InlineData("Cette offre se termine le 1er février 2027", "2027-02-01")]
    public void Parses_supported_localized_deal_end_dates(
        string text,
        string expected)
    {
        var parsed = DealEndDateParser.Parse(text);

        Assert.NotNull(parsed);
        Assert.Equal(
            DateOnly.ParseExact(expected, "yyyy-MM-dd"),
            parsed!.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Questa offerta termina presto")]
    [InlineData("31 febbraio 2026")]
    [InlineData("2026-13-40")]
    public void Invalid_dates_are_ignored(string text)
    {
        Assert.Null(DealEndDateParser.Parse(text));
    }
}
