using AmazonScraper.Core.Searching;

namespace AmazonScraper.Core.Tests;

public class PageSelectionTests
{
    [Theory]
    [InlineData("2", new[] { 2 })]
    [InlineData("1,3,5", new[] { 1, 3, 5 })]
    [InlineData("3-5", new[] { 3, 4, 5 })]
    [InlineData("1,3-5,8", new[] { 1, 3, 4, 5, 8 })]
    [InlineData("5,1,3,3", new[] { 1, 3, 5 })]
    [InlineData(" 1, 3 , 5 ", new[] { 1, 3, 5 })]
    public void Parse_valid_specs(string spec, int[] expected)
    {
        Assert.True(PageSelection.TryParse(spec, out var selection, out var error), error);
        Assert.Equal(expected, selection.Pages);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0")]
    [InlineData("21")]
    [InlineData("5-3")]
    [InlineData("1,,3")]
    [InlineData("1,")]
    [InlineData("a")]
    [InlineData("1-2-3")]
    [InlineData("-1")]
    [InlineData("1-")]
    [InlineData("1.5")]
    public void Parse_invalid_specs(string spec)
    {
        Assert.False(PageSelection.TryParse(spec, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void First_builds_pages_one_to_count()
    {
        var selection = PageSelection.First(4);

        Assert.Equal(new[] { 1, 2, 3, 4 }, selection.Pages);
        Assert.True(selection.IsMultiPage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void First_rejects_out_of_range_counts(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PageSelection.First(count));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Single_rejects_out_of_range_pages(int page)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PageSelection.Single(page));
    }
}
