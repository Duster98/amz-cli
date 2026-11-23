using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class JapanParserTests
{
    [Fact]
    public void Japan_search_uses_yen_and_japanese_ratings()
    {
        var html = Fixtures.Load("jp-search.html.gz");
        var results = new SearchParser(JapanMarketplace.Instance).Parse(html);

        Assert.Equal(58, results.Count);
        Assert.Equal(41, results.Count(r => r.Price.StartsWith("￥", StringComparison.Ordinal)));
        Assert.Equal(58, results.Count(r => !string.IsNullOrEmpty(r.Rating)));
        Assert.DoesNotContain(results, r => r.Title.StartsWith("スポンサー広告", StringComparison.Ordinal));

        var first = results[0];
        Assert.Equal("B0GZY6L66W", first.Asin);
        Assert.Equal("￥2,399", first.Price);
        Assert.Equal("4.4 / 5", first.Rating);
        Assert.Equal("2", first.Reviews);
    }

    [Fact]
    public void Japan_detail_uses_yen_and_canonical_url()
    {
        var html = Fixtures.Load("jp-detail.html.gz");
        var detail = new DetailParser(JapanMarketplace.Instance).Parse(html, "B0F5GWWKLP");

        Assert.Contains("マリオカート", detail.Title);
        Assert.Equal("任天堂", detail.Brand);
        Assert.StartsWith("￥", detail.Price);
        Assert.Equal("4.5 / 5", detail.Rating);
        Assert.Equal("1,446", detail.Reviews);
        Assert.StartsWith("Amazon", detail.Seller);
        Assert.StartsWith("Amazon", detail.ShippedBy);
        Assert.NotEmpty(detail.Images);
        Assert.NotEmpty(detail.Description);
        Assert.Equal("https://www.amazon.co.jp/dp/B0F5GWWKLP", detail.Url);
    }

    [Fact]
    public void Japan_physical_item_cleans_store_byline()
    {
        var html = Fixtures.Load("jp-rubik.html.gz");
        var detail = new DetailParser(JapanMarketplace.Instance).Parse(html, "B09J4ZPJ5K");

        Assert.Equal("GAN", detail.Brand);
        Assert.Equal("￥2,300", detail.Price);
        Assert.Equal("4.4 / 5", detail.Rating);
        Assert.Equal("1,107", detail.Reviews);
        Assert.NotEmpty(detail.Specs);
        Assert.NotEmpty(detail.Image);
        Assert.Equal("https://www.amazon.co.jp/dp/B09J4ZPJ5K", detail.Url);
    }

    [Fact]
    public void Japan_live_detail_uses_review_label_fallback()
    {
        var html = Fixtures.Load("jp-detail-live.html.gz");
        var detail = new DetailParser(JapanMarketplace.Instance).Parse(html, "B0F5GWWKLP");

        Assert.Equal("1,446", detail.Reviews);
        Assert.Equal("4.5 / 5", detail.Rating);
        Assert.Equal("￥7,992", detail.Price);
        Assert.Equal("https://www.amazon.co.jp/dp/B0F5GWWKLP", detail.Url);
    }

    [Fact]
    public void Japan_detail_prefers_price_to_pay_over_basis_price()
    {
        var html = Fixtures.Load("jp-apex-price.html.gz");
        var detail = new DetailParser(JapanMarketplace.Instance).Parse(html, "B0GT43P31B");

        Assert.Equal("￥2,298", detail.Price);
        Assert.Equal("￥3,819", detail.OriginalPrice);
        Assert.Equal("-40%", detail.Discount);
        Assert.Equal("B0GT43P31B", detail.Specs["ASIN"]);
        Assert.DoesNotContain(
            detail.Specs.Keys,
            key => key.Contains("売れ筋ランキング", StringComparison.Ordinal));
    }

    [Fact]
    public void Japan_search_prefers_strikethrough_list_price_over_unit_price()
    {
        var html = Fixtures.Load("jp-search-original.html.gz");
        var results = new SearchParser(JapanMarketplace.Instance).Parse(html);
        var item = results.First(r => r.Asin == "B0H82MMYG7");

        Assert.Equal("￥446", item.Price);
        Assert.Equal("￥770", item.OriginalPrice);
        Assert.Equal("-42%", item.Discount);
    }

    [Fact]
    public void Japan_search_drops_original_price_not_higher_than_current()
    {
        var html = """
            <div data-asin="B0CSCTBQPR" data-component-type="s-search-result">
              <h2 aria-label="テスト商品"></h2>
              <span class="a-price">
                <span class="a-offscreen">￥757</span>
                <span aria-hidden="true">
                  <span class="a-price-symbol">￥</span>
                  <span class="a-price-whole">757</span>
                </span>
              </span>
              <span class="a-price a-text-price" data-a-strike="true">
                <span class="a-offscreen">リスト: ￥757</span>
              </span>
            </div>
            """;

        var results = new SearchParser(JapanMarketplace.Instance).Parse(html);
        var item = results.First(r => r.Asin == "B0CSCTBQPR");

        Assert.Null(item.OriginalPrice);
        Assert.Null(item.Discount);
    }

    [Fact]
    public void Japan_detail_extracts_returns_from_warranty_table()
    {
        var html = """
            <html><body>
            <div id="productSpecifications_dp_warranty_and_support">
              <table id="productDetails_warranty_support_sections"
                     class="a-keyvalue prodDetTable" role="presentation">
                <span>返品</span>:<span class="a-letter-space"></span>
                <span>Amazon.co.jpで購入した商品について、30日以内の返品を受け付けます。</span>
              </table>
            </div>
            </body></html>
            """;

        var detail = new DetailParser(JapanMarketplace.Instance)
            .Parse(html, "B0HF82PZNB");

        Assert.Contains("30日以内", detail.Returns);
        Assert.Contains("返品", detail.Returns);
    }

    [Fact]
    public void Japan_review_text_starting_with_ato_is_not_delivery_cutoff()
    {
        var html =
            "<html><body><p>あとなこちゃんの内なる声みたいなデフォルメキャラの出番" +
            "序盤と比べて減りましたよね。脳内友達はリアル友達が出来ちゃったら" +
            "もう出番がないんでしょうか。</p></body></html>";
        var detail = new DetailParser(JapanMarketplace.Instance)
            .Parse(html, "B0HG1DF4X9");

        Assert.Null(detail.Delivery);
        Assert.Equal(string.Empty, detail.Availability);
    }

    [Fact]
    public void Japan_delivery_cutoff_still_matches_time_before_order()
    {
        var html =
            "<html><body><p>あと 2 時間 53 分以内にご注文の場合</p></body></html>";
        var detail = new DetailParser(JapanMarketplace.Instance)
            .Parse(html, "B0GT43P31B");

        Assert.NotNull(detail.Delivery);
        Assert.Contains("2 時間 53 分", detail.Delivery!.Cutoff);
    }

    [Fact]
    public void Japan_format_paths_and_cookie_force_yen()
    {
        Assert.Equal("￥2,399", JapaneseFormat.Instance.Price("2,399", string.Empty));
        Assert.Equal("￥8,980", JapaneseFormat.Instance.OriginalPrice("￥8,980"));
        Assert.True(JapaneseFormat.Instance.TryParsePrice("￥8,980", out var amount));
        Assert.Equal(8980m, amount);

        Assert.Equal(
            "https://www.amazon.co.jp/-/ja/s?k=joycon%20switch",
            JapanMarketplace.Instance.SearchUrl("joycon switch"));
        Assert.Equal("https://www.amazon.co.jp/-/ja/dp/B0F5GWWKLP", JapanMarketplace.Instance.DetailUrl("B0F5GWWKLP"));
        Assert.Equal("https://www.amazon.co.jp/dp/B0F5GWWKLP", JapanMarketplace.Instance.ProductUrl("B0F5GWWKLP"));
        Assert.Equal("i18n-prefs=JPY", JapanMarketplace.Instance.DefaultCookieHeader);
        Assert.False(JapanMarketplace.Instance.WarmUpEnabled);
    }
}
