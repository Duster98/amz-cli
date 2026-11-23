using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class HtmlTextTests
{
    [Fact]
    public void Clean_removes_inline_script_and_style_content()
    {
        const string html = """
            <div>4,6 <span>4,6 su 5 stelle</span> (3)
              <script type="text/javascript">
                var dpAcrHasRegisteredArcLinkClickAction;
                P.when('A', 'ready').execute(function(A) {});
              </script>
              <style>.hidden { display: none; }</style>
            </div>
            """;

        var text = HtmlText.Clean(html);

        Assert.Equal("4,6 4,6 su 5 stelle (3)", text);
        Assert.DoesNotContain("P.when", text);
        Assert.DoesNotContain("display", text);
    }

    [Fact]
    public void Clean_removes_html_comments_without_leaking_their_content()
    {
        var text = HtmlText.Clean(
            "<span>Prima</span><!-- commento <b>nascosto</b> --><span>Dopo</span>");

        Assert.Equal("Prima Dopo", text);
    }
}
