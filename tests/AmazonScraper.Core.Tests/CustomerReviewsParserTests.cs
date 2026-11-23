using AmazonScraper.Core.Formatting;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Models;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class CustomerReviewsParserTests
{
    [Fact]
    public void Parses_rating_summary_and_modern_top_reviews()
    {
        const string html = """
            <span id="acrPopover" title="4,5 su 5 stelle">4,5 su 5 stelle</span>
            <span id="acrCustomerReviewText" aria-label="1.191 Recensioni">1.191 Recensioni</span>
            <ul id="histogramTable">
              <li><a aria-label="77% delle recensioni hanno 5 stelle"
                     href="/portal/customer-reviews/B0C9C4GHGN/?filterByStar=five_star">
                <div aria-valuenow="77" class="a-meter"></div>
              </a></li>
              <li><a aria-label="11% delle recensioni hanno 4 stelle"
                     href="/portal/customer-reviews/B0C9C4GHGN/?filterByStar=four_star">
                <div aria-valuenow="11" class="a-meter"></div>
              </a></li>
              <li><a aria-label="6% delle recensioni hanno 3 stelle"
                     href="/portal/customer-reviews/B0C9C4GHGN/?filterByStar=three_star">
                <div aria-valuenow="6" class="a-meter"></div>
              </a></li>
              <li><a aria-label="1% delle recensioni hanno 2 stelle"
                     href="/portal/customer-reviews/B0C9C4GHGN/?filterByStar=two_star">
                <div aria-valuenow="1" class="a-meter"></div>
              </a></li>
              <li><a aria-label="5% delle recensioni hanno 1 stelle"
                     href="/portal/customer-reviews/B0C9C4GHGN/?filterByStar=one_star">
                <div aria-valuenow="5" class="a-meter"></div>
              </a></li>
            </ul>
            <ul id="localTopReviewsList">
              <li>
                <div class="a-section" data-hook="review" id="R3OKMMJPHEO04K">
                  <div data-hook="genome-widget">
                    <span class="a-profile-name">StellaCritica</span>
                  </div>
                  <i class="a-icon a-icon-star a-star-5" data-hook="review-star-rating">
                    <span class="a-icon-alt">5 su 5 stelle</span>
                  </i>
                  <a href="/portal/customer-reviews/srp/-/R3OKMMJPHEO04K/ref=cm_cr_dp_d_rvw_ttl">
                    <h5 data-hook="reviewTitle">Boost per il PC !</h5>
                  </a>
                  <span data-hook="review-date">Recensito in Italia il 13 settembre 2024</span>
                  <div data-hook="product-variation-attributes">
                    <span>Nome stile: EN600 pro</span>
                    <span>Capacità: 512GB</span>
                    <span data-hook="avp-badge">Acquisto verificato</span>
                  </div>
                  <div data-hook="reviewRichContentContainer">
                    <p>Primo paragrafo.</p>
                    <p>Secondo paragrafo.</p>
                  </div>
                  <span data-hook="helpful-vote-statement">9 persone l’hanno trovata utile</span>
                  <img data-hook="review-image-tile"
                       src="https://m.media-amazon.com/images/I/review-image.jpg"/>
                </div>
              </li>
            </ul>
            <ul id="internationalTopReviewsList">
              <li>
                <div class="a-section" data-hook="review" id="R10TT1892EEII0">
                  <span class="a-profile-name">M.J.</span>
                  <i class="a-icon a-icon-star a-star-4-5" data-hook="review-star-rating">
                    <span class="a-icon-alt">4,5 su 5 stelle</span>
                  </i>
                  <h5 data-hook="reviewTitle">Bin super zufrieden</h5>
                  <span data-hook="review-date">Recensito in Germania il 6 novembre 2024</span>
                  <div data-hook="reviewRichContentContainer">
                    <p>Sehr gute SSD.</p>
                  </div>
                </div>
              </li>
            </ul>
            """;

        var rating = CustomerReviewsParser.ParseRating(html, ItalyMarketplace.Instance);
        var reviews = CustomerReviewsParser.ParseReviews(html, ItalyMarketplace.Instance);
        var reviewsWithImages = CustomerReviewsParser.ParseReviews(
            html,
            ItalyMarketplace.Instance,
            includeImages: true);

        Assert.NotNull(rating);
        Assert.Equal(4.5, rating.Average);
        Assert.Equal(1191, rating.Count);
        Assert.Equal(["5", "4", "3", "2", "1"], rating.Histogram.Keys);
        Assert.Equal(77, rating.Histogram["5"]);
        Assert.Equal(5, rating.Histogram["1"]);

        Assert.NotNull(reviews);
        Assert.Equal(2, reviews.Count);
        Assert.Equal(1, reviews.LocalCount);
        Assert.Equal(1, reviews.InternationalCount);

        var local = reviews.Items[0];
        Assert.Equal("R3OKMMJPHEO04K", local.Id);
        Assert.Equal("StellaCritica", local.Author);
        Assert.Equal("5 / 5", local.Rating);
        Assert.Equal(5, local.RatingValue);
        Assert.Equal("Boost per il PC !", local.Title);
        Assert.Equal("Recensito in Italia il 13 settembre 2024", local.Date);
        Assert.Equal("local", local.Source);
        Assert.True(local.VerifiedPurchase);
        Assert.Equal("9 persone l’hanno trovata utile", local.Helpful);
        Assert.Equal("Nome stile: EN600 pro Capacità: 512GB", local.Variant);
        Assert.Equal("Primo paragrafo.\n\nSecondo paragrafo.", local.Body);
        Assert.Equal(
            "https://www.amazon.it/portal/customer-reviews/srp/-/R3OKMMJPHEO04K/ref=cm_cr_dp_d_rvw_ttl",
            local.Url);
        Assert.Null(local.Images);

        var international = reviews.Items[1];
        Assert.Equal("international", international.Source);
        Assert.Equal("4,5 / 5", international.Rating);
        Assert.Equal(4.5, international.RatingValue);
        Assert.False(international.VerifiedPurchase);

        Assert.NotNull(reviewsWithImages);
        Assert.Equal(
            ["https://m.media-amazon.com/images/I/review-image.jpg"],
            reviewsWithImages.Items[0].Images);
    }

    [Fact]
    public void Parses_japanese_rating_and_review()
    {
        const string html = """
            <span id="acrPopover" title="5つ星のうち4.7">5つ星のうち4.7</span>
            <span id="acrCustomerReviewText" aria-label="787 レビュー">787 レビュー</span>
            <ul id="localTopReviewsList">
              <li>
                <div data-hook="review" id="R30YJXVSJ60BH8">
                  <span class="a-profile-name">Parmita Shrestha</span>
                  <i class="a-icon a-icon-star a-star-5" data-hook="review-star-rating">
                    <span class="a-icon-alt">星5つ中5つ</span>
                  </i>
                  <h5 data-hook="reviewTitle">Value for money</h5>
                  <span data-hook="review-date">2026年10月30日に日本でレビュー済み</span>
                  <div data-hook="product-variation-attributes">
                    <span>容量: 1TB</span>
                    <span>スタイル: 990 PRO</span>
                    <span data-hook="avp-badge">Amazonで購入</span>
                  </div>
                  <div data-hook="reviewRichContentContainer">とても良い製品です。</div>
                </div>
              </li>
            </ul>
            """;

        var rating = CustomerReviewsParser.ParseRating(html, JapanMarketplace.Instance);
        var reviews = CustomerReviewsParser.ParseReviews(html, JapanMarketplace.Instance);

        Assert.NotNull(rating);
        Assert.Equal(4.7, rating.Average);
        Assert.Equal(787, rating.Count);
        Assert.Empty(rating.Histogram);

        Assert.NotNull(reviews);
        var review = Assert.Single(reviews.Items);
        Assert.Equal("5 / 5", review.Rating);
        Assert.Equal(5, review.RatingValue);
        Assert.Equal("2026年10月30日に日本でレビュー済み", review.Date);
        Assert.Equal("容量: 1TB スタイル: 990 PRO", review.Variant);
        Assert.Equal("とても良い製品です。", review.Body);
    }

    [Fact]
    public void Parses_legacy_review_list_and_removes_ui_noise()
    {
        const string html = """
            <div id="cm-cr-dp-review-list">
              <div data-hook="review" id="RLEGACY123">
                <span class="a-profile-name">Old Reviewer</span>
                <i class="a-icon a-icon-star a-star-4" data-hook="review-rating">
                  <span class="a-icon-alt">4 out of 5 stars</span>
                </i>
                <a data-hook="review-title" href="/gp/customer-reviews/RLEGACY123">
                  <span>4 out of 5 stars</span> Good product
                </a>
                <span data-hook="review-date">Reviewed in the United Kingdom on 1 January 2025</span>
                <span data-hook="avp-badge">Verified Purchase</span>
                <span data-hook="review-body">
                  Useful text. Brief content visible, double tap to read full content.
                  Visualizza altro Read more
                </span>
              </div>
            </div>
            """;

        var reviews = CustomerReviewsParser.ParseReviews(
            html,
            UnitedKingdomMarketplace.Instance);

        Assert.NotNull(reviews);
        var review = Assert.Single(reviews.Items);
        Assert.Equal("local", review.Source);
        Assert.Equal("4 / 5", review.Rating);
        Assert.Equal(4, review.RatingValue);
        Assert.Equal("Good product", review.Title);
        Assert.Equal("Useful text.", review.Body);
        Assert.True(review.VerifiedPurchase);
        Assert.Equal(
            "https://www.amazon.co.uk/gp/customer-reviews/RLEGACY123",
            review.Url);
    }

    [Fact]
    public void Review_body_drops_replacement_characters()
    {
        const string html = """
            <div id="localTopReviewsList">
              <div data-hook="review" id="RREPLACEMENT1">
                <span class="a-profile-name">Test</span>
                <i data-hook="review-star-rating" class="a-icon a-icon-star a-star-5">
                  <span class="a-icon-alt">5,0 su 5 stelle</span>
                </i>
                <h5 data-hook="reviewTitle">Titolo</h5>
                <span data-hook="review-date">Recensito in Italia il 1 novembre 2026</span>
                <div data-hook="reviewRichContentContainer">
                  <p>Testo � con carattere non valido.</p>
                </div>
              </div>
            </div>
            """;

        var reviews = CustomerReviewsParser.ParseReviews(
            html,
            ItalyMarketplace.Instance);

        Assert.NotNull(reviews);
        var review = Assert.Single(reviews.Items);
        Assert.Equal("Testo con carattere non valido.", review.Body);
    }

    [Fact]
    public void No_reviews_returns_null_and_rating_without_histogram_is_kept()
    {
        const string html = """
            <span id="acrPopover" title="4,5 su 5 stelle"></span>
            """;

        var rating = CustomerReviewsParser.ParseRating(html, ItalyMarketplace.Instance);
        var reviews = CustomerReviewsParser.ParseReviews(html, ItalyMarketplace.Instance);

        Assert.NotNull(rating);
        Assert.Equal(4.5, rating.Average);
        Assert.Null(rating.Count);
        Assert.Empty(rating.Histogram);
        Assert.Null(reviews);
    }

    [Fact]
    public void Deduplicates_reviews_by_id_and_applies_limit()
    {
        const string html = """
            <ul id="localTopReviewsList">
              <li><div data-hook="review" id="R1">
                <span class="a-profile-name">One</span>
                <h5 data-hook="reviewTitle">First</h5>
              </div></li>
              <li><div data-hook="review" id="R2">
                <span class="a-profile-name">Two</span>
                <h5 data-hook="reviewTitle">Second</h5>
              </div></li>
            </ul>
            <ul id="internationalTopReviewsList">
              <li><div data-hook="review" id="R1">
                <span class="a-profile-name">One duplicate</span>
                <h5 data-hook="reviewTitle">Duplicate</h5>
              </div></li>
            </ul>
            """;

        var all = CustomerReviewsParser.ParseReviews(html, ItalyMarketplace.Instance);
        var limited = CustomerReviewsParser.ParseReviews(
            html,
            ItalyMarketplace.Instance,
            limit: 1);

        Assert.NotNull(all);
        Assert.Equal(2, all.Count);
        Assert.Equal(2, all.LocalCount);
        Assert.Equal(0, all.InternationalCount);

        Assert.NotNull(limited);
        Assert.Single(limited.Items);
        Assert.Equal("R1", limited.Items[0].Id);
    }

    [Fact]
    public void Rating_falls_back_to_localized_detail_icon()
    {
        const string html = """
            <i class="a-icon a-icon-star a-star-4-5">
              <span class="a-icon-alt">4,6 su 5 stelle</span>
            </i>
            """;

        var rating = CustomerReviewsParser.ParseRating(html, ItalyMarketplace.Instance);

        Assert.NotNull(rating);
        Assert.Equal(4.6, rating.Average);
    }

    [Theory]
    [InlineData("4,5 su 5 stelle", 4.5)]
    [InlineData("4.8 out of 5 stars", 4.8)]
    [InlineData("5つ星のうち4.7", 4.7)]
    public void Rating_summary_text_handles_localized_order(string text, double expected)
    {
        var html = $"""<span id="acrPopover" title="{text}"></span>""";

        var rating = CustomerReviewsParser.ParseRating(html, ItalyMarketplace.Instance);

        Assert.NotNull(rating);
        Assert.Equal(expected, rating.Average);
    }

    [Fact]
    public void International_review_without_link_gets_canonical_url()
    {
        const string html = """
            <div id="internationalTopReviewsList">
              <div data-hook="review" id="R1DPHENLXBEAFU">
                <span class="a-profile-name">Guppy</span>
                <i data-hook="review-star-rating" class="a-icon a-icon-star a-star-5">
                  <span class="a-icon-alt">5 de 5 estrellas</span>
                </i>
                <h5 data-hook="reviewTitle">Perfekt</h5>
                <span data-hook="review-date">Reseñado en Alemania el 2 de noviembre de 2026</span>
                <div data-hook="reviewRichContentContainer"><span>Gut</span></div>
              </div>
            </div>
            """;
        var reviews = CustomerReviewsParser.ParseReviews(
            html,
            SpainMarketplace.Instance);

        Assert.NotNull(reviews);
        var item = Assert.Single(reviews.Items);
        Assert.Equal(
            "https://www.amazon.es/gp/customer-reviews/R1DPHENLXBEAFU",
            item.Url);
    }

    [Fact]
    public void France_detail_review_count_keeps_space_separator()
    {
        const string html = "<span id=\"acrCustomerReviewText\">(15\u00a0688)</span>";

        var detail = new DetailParser(FranceMarketplace.Instance)
            .Parse(html, "B0B9C3ZVHR");

        Assert.Equal("15 688", detail.Reviews);
    }

    [Fact]
    public void Histogram_fills_missing_star_rows_with_zero()
    {
        const string html = """
            <ul id="histogramTable">
              <li><a href="/portal/customer-reviews/B0C9C4GHGN/?filterByStar=five_star">
                <div aria-valuenow="91" class="a-meter"></div>
              </a></li>
              <li><a href="/portal/customer-reviews/B0C9C4GHGN/?filterByStar=one_star">
                <div aria-valuenow="2" class="a-meter"></div>
              </a></li>
            </ul>
            """;

        var rating = CustomerReviewsParser.ParseRating(html, ItalyMarketplace.Instance);

        Assert.NotNull(rating);
        Assert.Equal(["5", "4", "3", "2", "1"], rating.Histogram.Keys);
        Assert.Equal(91, rating.Histogram["5"]);
        Assert.Equal(0, rating.Histogram["4"]);
        Assert.Equal(0, rating.Histogram["3"]);
        Assert.Equal(0, rating.Histogram["2"]);
        Assert.Equal(2, rating.Histogram["1"]);
    }

    [Fact]
    public void Tsv_and_json_expose_requested_rating_and_reviews()
    {
        var detail = new ProductDetail
        {
            Asin = "B0C9C4GHGN",
            Title = "Ediloca",
            Rating = "4,5 / 5",
            Reviews = "1.191",
            RatingDetails = new RatingDetails
            {
                Average = 4.5,
                Count = 1191,
                Histogram = new Dictionary<string, int>
                {
                    ["5"] = 77,
                    ["4"] = 11,
                    ["3"] = 6,
                    ["2"] = 1,
                    ["1"] = 5,
                },
            },
            TopReviews = new TopReviews
            {
                Items =
                [
                    new ProductReview
                    {
                        Id = "R1",
                        Author = "StellaCritica",
                        Rating = "5 / 5",
                        RatingValue = 5,
                        Title = "Boost per il PC !",
                        Date = "Recensito in Italia il 13 settembre 2024",
                        Source = "local",
                        VerifiedPurchase = true,
                        Helpful = "9 persone l’hanno trovata utile",
                        Variant = "Capacità: 512GB",
                        Body = "Testo\ncon tab\t e nuova riga.",
                        Url = "https://www.amazon.it/portal/customer-reviews/srp/-/R1",
                        Images = ["https://m.media-amazon.com/images/I/review.jpg"],
                    },
                ],
            },
        };

        var tsv = TsvFormatter.Detail(
            detail,
            includeRating: true,
            includeReviews: true);
        var json = JsonFormatter.Detail(detail);

        Assert.Contains("RatingAverage\t4.5", tsv);
        Assert.Contains("RatingCount\t1191", tsv);
        Assert.Contains("Rating5\t77", tsv);
        Assert.Contains("ReviewsAvailable\t1", tsv);
        Assert.Contains("ReviewsLocal\t1", tsv);
        Assert.Contains("ReviewImage\tR1\thttps://m.media-amazon.com/images/I/review.jpg", tsv);
        Assert.Contains("Testo con tab e nuova riga.", tsv);
        Assert.Contains("\"rating_details\"", json);
        Assert.Contains("\"average\": 4.5", json);
        Assert.Contains("\"top_reviews\"", json);
        Assert.Contains("\"count\": 1", json);

        var defaultTsv = TsvFormatter.Detail(detail);
        var defaultJson = JsonFormatter.Detail(new ProductDetail { Asin = "B0C9C4GHGN" });

        Assert.DoesNotContain("RatingAverage", defaultTsv);
        Assert.DoesNotContain("ReviewsAvailable", defaultTsv);
        Assert.DoesNotContain("rating_details", defaultJson);
        Assert.DoesNotContain("top_reviews", defaultJson);
    }
}
