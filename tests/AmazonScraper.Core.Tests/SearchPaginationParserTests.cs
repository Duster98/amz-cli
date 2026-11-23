using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class SearchPaginationParserTests
{
    [Theory]
    [InlineData("us-search.html.gz", 20)]
    [InlineData("de-search.html.gz", 20)]
    [InlineData("fr-search.html.gz", 7)]
    [InlineData("es-search.html.gz", 5)]
    [InlineData("jp-search.html.gz", 10)]
    [InlineData("uk-search.html.gz", 5)]
    public void Reads_the_last_page_from_the_pagination_strip(
        string fixture,
        int expected)
    {
        var html = Fixtures.Load(fixture);

        Assert.Equal(expected, SearchPaginationParser.ParseTotalPages(html));
    }

    [Fact]
    public void Returns_null_when_the_result_page_has_no_pagination()
    {
        var html = Fixtures.Load("search.html.gz");

        Assert.Null(SearchPaginationParser.ParseTotalPages(html));
    }

    [Fact]
    public void Clamps_an_implausible_page_count()
    {
        var html =
            """
            <span class="s-pagination-strip">
              <span class="s-pagination-item s-pagination-disabled">99</span>
            </span></div>
            """;

        Assert.Equal(20, SearchPaginationParser.ParseTotalPages(html));
    }
}
