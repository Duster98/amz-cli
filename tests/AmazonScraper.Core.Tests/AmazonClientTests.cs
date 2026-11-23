using System.Net;
using System.Text;
using System.Text.Json;
using AmazonScraper.Core.Http;
using AmazonScraper.Core.Marketplaces;

namespace AmazonScraper.Core.Tests;

public class AmazonClientTests
{
    [Fact]
    public void Challenge_detection_uses_both_markers()
    {
        Assert.True(AmazonClient.IsChallenge("<script>bm-verify</script> /_sec/verify"));
        Assert.False(AmazonClient.IsChallenge("<html>normal page</html>"));
    }

    [Theory]
    [InlineData("/_sec/verify")]
    [InlineData("/errors/validateCaptcha")]
    [InlineData("captchacharacters")]
    [InlineData("api-services-support@amazon.com")]
    public void Block_detection_recognizes_known_markers(string marker)
    {
        Assert.True(AmazonClient.IsBlocked($"<html>{marker}</html>"));
        Assert.False(AmazonClient.IsBlocked("<html>normal page</html>"));
    }

    [Fact]
    public void Waf_challenge_detection_recognizes_aws_waf_page()
    {
        const string html = """
            <script>window.awsWafCookieDomainList = []; window.gokuProps = {};</script>
            <script src="https://example/challenge.js"></script>
            """;
        Assert.True(AmazonClient.IsWafChallenge(html));
        Assert.False(AmazonClient.IsWafChallenge("<html>normal page</html>"));
    }

    [Fact]
    public async Task Search_solves_challenge_and_retries_once()
    {
        var requests = new List<string>();
        var bodies = new List<string>();
        var challenge = """
            <html>
              <script>
                var i = 5; var j = i + Number("12" + "3");
                xhr.send(JSON.stringify({"bm-verify": "TOKEN", "pow": i + j}));
              </script>
              <span>bm-verify /_sec/verify</span>
            </html>
            """;

        var handler = new StubHandler(request =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            if (request.Method == HttpMethod.Post)
            {
                bodies.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"reload\":true}", Encoding.UTF8, "application/json"),
                };
            }

            if (request.RequestUri!.AbsolutePath == "/")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html>home</html>"),
                };
            }

            if (requests.Count == 2)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(challenge),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>search result</html>"),
            };
        });

        using var http = new HttpClient(handler);
        using var client = new AmazonClient(
            ItalyMarketplace.Instance,
            new AmazonOptions { TimeoutSeconds = 5 },
            http);

        var html = await client.SearchAsync("rtx 5080");

        Assert.Equal("<html>search result</html>", html);
        Assert.Equal(4, requests.Count);
        Assert.Equal("https://www.amazon.it/", requests[0]);
        Assert.Equal("https://www.amazon.it/s?k=rtx%205080", requests[1]);
        Assert.Equal("https://www.amazon.it/_sec/verify?provider=interstitial", requests[2]);
        Assert.Equal("https://www.amazon.it/s?k=rtx%205080", requests[3]);
        Assert.Contains("\"bm-verify\":\"TOKEN\"", bodies[0]);
        Assert.Contains("\"pow\":128", bodies[0]);
    }

    [Fact]
    public async Task Search_page_two_requests_page_parameter()
    {
        var requests = new List<string>();
        var handler = new StubHandler(request =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            if (request.RequestUri.AbsolutePath == "/")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html>home</html>"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>search result</html>"),
            };
        });

        using var http = new HttpClient(handler);
        using var client = new AmazonClient(ItalyMarketplace.Instance, new AmazonOptions(), http);

        var html = await client.SearchPageAsync("asus", 2);

        Assert.Equal("<html>search result</html>", html);
        Assert.Equal("https://www.amazon.it/s?k=asus&page=2", requests[^1]);
    }

    [Fact]
    public async Task Navigation_headers_are_only_sent_for_uk_searches()
    {
        var seen = new List<(string Host, bool HasFetch, bool HasReferer)>();
        var handler = new StubHandler(request =>
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath.EndsWith("/s", StringComparison.Ordinal))
            {
                seen.Add((
                    uri.Host,
                    request.Headers.Contains("Sec-Fetch-Dest"),
                    request.Headers.Referrer is not null));
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>search result</html>"),
            };
        });

        using var http = new HttpClient(handler);
        using var ukClient = new AmazonClient(
            UnitedKingdomMarketplace.Instance,
            new AmazonOptions { Transport = AmazonTransport.Http },
            http);
        using var itClient = new AmazonClient(
            ItalyMarketplace.Instance,
            new AmazonOptions { Transport = AmazonTransport.Http },
            http);

        await ukClient.SearchPageAsync("coffee machine", 1);
        await itClient.SearchPageAsync("cuffie", 1);

        var uk = Assert.Single(seen, item => item.Host == "www.amazon.co.uk");
        var it = Assert.Single(seen, item => item.Host == "www.amazon.it");
        Assert.True(uk.HasFetch);
        Assert.True(uk.HasReferer);
        Assert.False(it.HasFetch);
        Assert.False(it.HasReferer);
    }

    [Fact]
    public async Task Uk_search_warms_up_once_before_search()
    {
        var requests = new List<(string Url, bool HasFetch)>();
        var handler = new StubHandler(request =>
        {
            requests.Add((
                request.RequestUri!.AbsoluteUri,
                request.Headers.Contains("Sec-Fetch-Dest")));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>ok</html>"),
            };
        });

        using var http = new HttpClient(handler);
        using var client = new AmazonClient(
            UnitedKingdomMarketplace.Instance,
            new AmazonOptions { Transport = AmazonTransport.Http },
            http);

        await client.SearchAsync("coffee machine");
        await client.SearchPageAsync("coffee machine", 2);

        Assert.Equal(3, requests.Count);
        Assert.Equal("https://www.amazon.co.uk/-/en-gb/", requests[0].Url);
        Assert.True(requests[0].HasFetch);
        Assert.Equal("https://www.amazon.co.uk/s?k=coffee%20machine", requests[1].Url);
        Assert.True(requests[1].HasFetch);
        Assert.Equal("https://www.amazon.co.uk/s?k=coffee%20machine&page=2", requests[2].Url);
    }

    [Fact]
    public async Task Search_page_accepts_a_deal_filter()
    {
        var requests = new List<string>();
        var handler = new StubHandler(request =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            if (request.RequestUri.AbsolutePath == "/")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html>home</html>"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>search result</html>"),
            };
        });

        using var http = new HttpClient(handler);
        using var client = new AmazonClient(ItalyMarketplace.Instance, new AmazonOptions(), http);

        var html = await client.SearchPageAsync("asus", 1, "26901109031");

        Assert.Equal("<html>search result</html>", html);
        Assert.Equal(
            "https://www.amazon.it/s?k=asus&rh=p_n_deal_type%3A26901109031",
            requests[^1]);
    }

    [Fact]
    public async Task Deals_page_uses_the_queryless_filter()
    {
        var requests = new List<string>();
        var handler = new StubHandler(request =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            if (request.RequestUri.AbsolutePath == "/")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html>home</html>"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>deals</html>"),
            };
        });

        using var http = new HttpClient(handler);
        using var client = new AmazonClient(ItalyMarketplace.Instance, new AmazonOptions(), http);

        var html = await client.DealsPageAsync("26901109031", 1);

        Assert.Equal("<html>deals</html>", html);
        Assert.Equal(
            "https://www.amazon.it/s?rh=p_n_deal_type%3A26901109031",
            requests[^1]);
    }

    [Fact]
    public async Task Http_block_invokes_the_transport_failure_callback()
    {
        string? reason = null;
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("busy"),
            });

        using var http = new HttpClient(handler);
        var options = new AmazonOptions
        {
            Transport = AmazonTransport.Http,
            OnHttpBlocked = value => reason = value,
        };
        using var client = new AmazonClient(
            UnitedStatesMarketplace.Instance,
            options,
            http);

        await Assert.ThrowsAsync<AmazonBlockedException>(
            () => client.SearchPageAsync("usb hub", 1));

        Assert.NotNull(reason);
        Assert.Contains("503", reason!);
    }

    [Fact]
    public async Task Curl_transport_does_not_invoke_the_http_failure_callback()
    {
        string? reason = null;
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("busy"),
            });

        using var http = new HttpClient(handler);
        var options = new AmazonOptions
        {
            Transport = AmazonTransport.Curl,
            OnHttpBlocked = value => reason = value,
        };
        using var client = new AmazonClient(
            UnitedStatesMarketplace.Instance,
            options,
            http);

        await Assert.ThrowsAsync<AmazonBlockedException>(
            () => client.SearchPageAsync("usb hub", 1));

        Assert.Null(reason);
    }

    [Fact]
    public async Task Sorted_search_requests_sort_parameter()
    {
        var requests = new List<string>();
        var handler = new StubHandler(request =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            if (request.RequestUri.AbsolutePath == "/")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html>home</html>"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>search result</html>"),
            };
        });

        using var http = new HttpClient(handler);
        using var client = new AmazonClient(
            ItalyMarketplace.Instance,
            new AmazonOptions(),
            http);

        var html = await client.SearchPageSortedAsync(
            "asus",
            2,
            "price-asc-rank");

        Assert.Equal("<html>search result</html>", html);
        Assert.Equal(
            "https://www.amazon.it/s?k=asus&page=2&s=price-asc-rank",
            requests[^1]);
    }

    [Fact]
    public async Task Search_page_zero_is_rejected()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>home</html>"),
        });
        using var http = new HttpClient(handler);
        using var client = new AmazonClient(ItalyMarketplace.Instance, new AmazonOptions(), http);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.SearchPageAsync("asus", 0));
    }

    [Fact]
    public async Task Buying_options_request_uses_aod_endpoint_and_ajax_headers()
    {
        var aodRequests = 0;
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html>home</html>"),
                };
            }

            aodRequests++;
            Assert.Equal(
                "https://www.amazon.it/gp/product/ajax/aodAjaxMain/ref=dp_aod_unknown_mbc" +
                "?asin=B07R3YS8R2&m=&qid=&smid=&sourcecustomerorglistid=" +
                "&sourcecustomerorglistitemid=&sr=&pc=dp",
                request.RequestUri.AbsoluteUri);
            Assert.Equal("XMLHttpRequest", request.Headers.GetValues("X-Requested-With").Single());
            Assert.Equal(
                "https://www.amazon.it/dp/B07R3YS8R2",
                request.Headers.Referrer!.AbsoluteUri);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<div id=\"aod-offer\">offer</div>"),
            };
        });

        using var http = new HttpClient(handler);
        using var client = new AmazonClient(
            ItalyMarketplace.Instance,
            new AmazonOptions { TimeoutSeconds = 5 },
            http);

        var html = await client.FetchBuyingOptionsAsync("B07R3YS8R2");

        Assert.Contains("aod-offer", html);
        Assert.Equal(1, aodRequests);
    }

    [Fact]
    public async Task Buying_options_page_two_uses_pagination_url()
    {
        var requests = new List<string>();
        var handler = new StubHandler(request =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            if (request.RequestUri.AbsolutePath == "/")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html>home</html>"),
                };
            }

            Assert.Contains("/ref=aod_page_2?", request.RequestUri.AbsoluteUri);
            Assert.Contains("isonlyrenderofferlist=true", request.RequestUri.AbsoluteUri);
            Assert.Contains("pageno=2", request.RequestUri.AbsoluteUri);
            Assert.Equal("XMLHttpRequest", request.Headers.GetValues("X-Requested-With").Single());

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<div id=\"aod-offer\">page 2</div>"),
            };
        });

        using var http = new HttpClient(handler);
        using var client = new AmazonClient(
            ItalyMarketplace.Instance,
            new AmazonOptions { TimeoutSeconds = 5 },
            http);

        var html = await client.FetchBuyingOptionsPageAsync("B08JH7KQKV", 2);

        Assert.Contains("page 2", html);
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task Seller_detail_request_uses_smid_and_psc()
    {
        var requests = new List<string>();
        var handler = new StubHandler(request =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            if (request.RequestUri.AbsolutePath == "/")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html>home</html>"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>seller page</html>"),
            };
        });

        using var http = new HttpClient(handler);
        using var client = new AmazonClient(
            ItalyMarketplace.Instance,
            new AmazonOptions { TimeoutSeconds = 5 },
            http);

        var html = await client.SellerDetailAsync("B07R3YS8R2", "A131AJORA55M3D");

        Assert.Equal("<html>seller page</html>", html);
        Assert.Equal(2, requests.Count);
        Assert.Equal(
            "https://www.amazon.it/dp/B07R3YS8R2?smid=A131AJORA55M3D&psc=1",
            requests[1]);
    }

    [Fact]
    public async Task Set_delivery_location_runs_glow_flow()
    {
        var requests = new List<(string Url, string Body)>();
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            var body = request.Content is null
                ? string.Empty
                : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            requests.Add((url, body));

            if (request.RequestUri.AbsolutePath.StartsWith("/dp/", StringComparison.Ordinal))
            {
                return JsonHtml("""
                    <input id="glowValidationToken" value="GLOW123" />
                    """);
            }

            if (url.Contains("get-rendered-address-selections", StringComparison.Ordinal))
            {
                Assert.Equal("GLOW123", request.Headers.GetValues("anti-csrftoken-a2z").Single());
                Assert.Equal(
                    "https://www.amazon.co.uk/dp/B0BY2R4BHW",
                    request.Headers.Referrer!.AbsoluteUri);
                Assert.Equal("XMLHttpRequest", request.Headers.GetValues("X-Requested-With").Single());
                Assert.Contains("text/html", request.Headers.Accept.ToString());
                return JsonHtml("""
                    <script>GLUXWidget = { CSRF_TOKEN : "CSRF123" };</script>
                    """);
            }

            if (url.Contains("address-change", StringComparison.Ordinal))
            {
                Assert.Equal("CSRF123", request.Headers.GetValues("anti-csrftoken-a2z").Single());
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"isValidAddress":1,"isAddressUpdated":1,"successful":1}""",
                        Encoding.UTF8,
                        "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var http = new HttpClient(handler);
        using var client = new AmazonClient(
            UnitedKingdomMarketplace.Instance,
            new AmazonOptions { Transport = AmazonTransport.Http, GlowStepDelayMs = 0 },
            http);

        var result = await client.SetDeliveryLocationAsync(
            "https://www.amazon.co.uk/dp/B0BY2R4BHW",
            "E1 6AN",
            "GB");

        Assert.True(result);
        Assert.Equal(4, requests.Count);
        Assert.Equal("https://www.amazon.co.uk/-/en-gb/", requests[0].Url);
        var addressChange = Assert.Single(
            requests,
            item => item.Url.Contains("address-change", StringComparison.Ordinal));
        Assert.Contains("locationType=LOCATION_INPUT", addressChange.Body);
        Assert.Contains("zipCode=E1", addressChange.Body);
        Assert.DoesNotContain("countryCode=", addressChange.Body);
    }

    [Fact]
    public async Task Set_delivery_location_spain_uses_json_glow_flow()
    {
        const string seedHtml = """
            <input id="glowValidationToken" value="GLOW123" />
            <div data-a-popover="{&quot;name&quot;:&quot;glow-modal&quot;,&quot;url&quot;:&quot;/portal-migration/hz/glow/get-rendered-address-selections?deviceType&#x3D;desktop&amp;pageType&#x3D;Detail&amp;storeContext&#x3D;computers&amp;actionSource&#x3D;desktop-modal&quot;}"></div>
            """;

        var requests = new List<string>();
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            requests.Add(url);

            if (request.RequestUri.AbsolutePath == "/-/es/")
            {
                return JsonHtml("<html>home</html>");
            }

            if (request.RequestUri.AbsolutePath.StartsWith("/dp/", StringComparison.Ordinal))
            {
                return JsonHtml(seedHtml);
            }

            if (url.Contains("get-rendered-address-selections", StringComparison.Ordinal))
            {
                Assert.Contains("pageType=Detail", url);
                Assert.Contains("storeContext=computers", url);
                Assert.Equal(
                    "https://www.amazon.es/dp/B0DTJFZ4YS",
                    request.Headers.Referrer!.AbsoluteUri);
                Assert.Equal("XMLHttpRequest", request.Headers.GetValues("X-Requested-With").Single());
                Assert.Contains("text/html", request.Headers.Accept.ToString());
                return JsonHtml("""<script>CSRF_TOKEN : "CSRF123"</script>""");
            }

            if (url.Contains("address-change", StringComparison.Ordinal))
            {
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                Assert.Equal(
                    "application/json",
                    request.Content.Headers.ContentType!.MediaType);
                Assert.Contains("\"zipCode\":\"28010\"", body);
                Assert.Contains("\"pageType\":\"Detail\"", body);
                Assert.Contains("\"storeContext\":\"computers\"", body);
                Assert.DoesNotContain("almBrandId", body);
                Assert.Equal(
                    "https://www.amazon.es/dp/B0DTJFZ4YS",
                    request.Headers.Referrer!.AbsoluteUri);
                Assert.Equal(
                    "https://www.amazon.es",
                    request.Headers.GetValues("Origin").Single());
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"isValidAddress":1,"successful":1}""",
                        Encoding.UTF8,
                        "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var http = new HttpClient(handler);
        using var client = new AmazonClient(
            SpainMarketplace.Instance,
            new AmazonOptions { Transport = AmazonTransport.Http, GlowStepDelayMs = 0 },
            http);

        var result = await client.SetDeliveryLocationAsync(
            "https://www.amazon.es/dp/B0DTJFZ4YS",
            "28010",
            "ES");

        Assert.True(result);
        Assert.Equal(4, requests.Count);
        Assert.EndsWith("/-/es/", requests[0]);
    }

    [Fact]
    public async Task Set_delivery_location_includes_country_for_cross_border()
    {
        string? postedBody = null;
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (request.RequestUri.AbsolutePath.StartsWith("/dp/", StringComparison.Ordinal))
            {
                return JsonHtml("""<input id="glowValidationToken" value="GLOW123" />""");
            }

            if (url.Contains("get-rendered-address-selections", StringComparison.Ordinal))
            {
                return JsonHtml("""<script>CSRF_TOKEN : "CSRF123"</script>""");
            }

            if (url.Contains("address-change", StringComparison.Ordinal))
            {
                postedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"isValidAddress":1,"successful":1}""",
                        Encoding.UTF8,
                        "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var http = new HttpClient(handler);
        using var client = new AmazonClient(
            UnitedKingdomMarketplace.Instance,
            new AmazonOptions { Transport = AmazonTransport.Http, GlowStepDelayMs = 0 },
            http);

        var result = await client.SetDeliveryLocationAsync(
            "https://www.amazon.co.uk/dp/B0BY2R4BHW",
            "20121",
            "IT");

        Assert.True(result);
        Assert.NotNull(postedBody);
        Assert.Contains("countryCode=IT", postedBody);
    }

    [Fact]
    public async Task Set_delivery_location_us_international_uses_country_only_payload()
    {
        string? postedBody = null;
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (request.RequestUri.AbsolutePath.StartsWith("/dp/", StringComparison.Ordinal))
            {
                return JsonHtml("""<input id="glowValidationToken" value="GLOW123" />""");
            }

            if (url.Contains("get-rendered-address-selections", StringComparison.Ordinal))
            {
                Assert.Contains("pageType=exports-gateway-desktop", url);
                return JsonHtml("""<script>CSRF_TOKEN : "CSRF123"</script>""");
            }

            if (url.Contains("address-change", StringComparison.Ordinal))
            {
                postedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"isValidAddress":1,"successful":1}""",
                        Encoding.UTF8,
                        "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var http = new HttpClient(handler);
        using var client = new AmazonClient(
            UnitedStatesMarketplace.Instance,
            new AmazonOptions { Transport = AmazonTransport.Http, GlowStepDelayMs = 0 },
            http);

        var result = await client.SetDeliveryLocationAsync(
            "https://www.amazon.com/dp/B0H4SBSYBS",
            string.Empty,
            "IT");

        Assert.True(result);
        Assert.NotNull(postedBody);
        using var document = JsonDocument.Parse(postedBody!);
        var root = document.RootElement;
        Assert.Equal("COUNTRY", root.GetProperty("locationType").GetString());
        Assert.Equal("IT", root.GetProperty("district").GetString());
        Assert.Equal("IT", root.GetProperty("countryCode").GetString());
        Assert.Equal("exports-gateway-desktop", root.GetProperty("pageType").GetString());
        Assert.Equal("generic", root.GetProperty("storeContext").GetString());
        Assert.False(root.TryGetProperty("zipCode", out _));
    }

    [Fact]
    public async Task Set_delivery_location_japan_international_uses_country_only_payload()
    {
        const string seedHtml = """
            <input id="glowValidationToken" value="GLOW123" />
            <div data-a-popover="{&quot;name&quot;:&quot;glow-modal&quot;,&quot;url&quot;:&quot;/portal-migration/hz/glow/get-rendered-address-selections?deviceType&#x3D;desktop&amp;pageType&#x3D;Detail&amp;storeContext&#x3D;diy&amp;actionSource&#x3D;desktop-modal&quot;}"></div>
            """;
        string? postedBody = null;
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (request.RequestUri.AbsolutePath.StartsWith("/dp/", StringComparison.Ordinal))
            {
                return JsonHtml(seedHtml);
            }

            if (url.Contains("get-rendered-address-selections", StringComparison.Ordinal))
            {
                Assert.Contains("pageType=Detail", url);
                Assert.Contains("storeContext=diy", url);
                return JsonHtml("""<script>CSRF_TOKEN : "CSRF123"</script>""");
            }

            if (url.Contains("address-change", StringComparison.Ordinal))
            {
                postedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"isValidAddress":1,"successful":1}""",
                        Encoding.UTF8,
                        "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var http = new HttpClient(handler);
        using var client = new AmazonClient(
            JapanMarketplace.Instance,
            new AmazonOptions { Transport = AmazonTransport.Http, GlowStepDelayMs = 0 },
            http);

        var result = await client.SetDeliveryLocationAsync(
            "https://www.amazon.co.jp/dp/B0F5GWWKLP",
            string.Empty,
            "IT");

        Assert.True(result);
        Assert.NotNull(postedBody);
        using var document = JsonDocument.Parse(postedBody!);
        var root = document.RootElement;
        Assert.Equal("COUNTRY", root.GetProperty("locationType").GetString());
        Assert.Equal("IT", root.GetProperty("district").GetString());
        Assert.Equal("IT", root.GetProperty("countryCode").GetString());
        Assert.Equal("Detail", root.GetProperty("pageType").GetString());
        Assert.Equal("diy", root.GetProperty("storeContext").GetString());
        Assert.False(root.TryGetProperty("zipCode", out _));
    }

    [Fact]
    public async Task Set_delivery_location_us_zip_uses_exports_gateway_and_zip_payload()
    {
        string? postedBody = null;
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (request.RequestUri.AbsolutePath.StartsWith("/dp/", StringComparison.Ordinal))
            {
                return JsonHtml("""<input id="glowValidationToken" value="GLOW123" />""");
            }

            if (url.Contains("get-rendered-address-selections", StringComparison.Ordinal))
            {
                if (url.Contains("pageType=Gateway", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                Assert.Contains("pageType=exports-gateway-desktop", url);
                return JsonHtml("""<script>CSRF_TOKEN : "CSRF123"</script>""");
            }

            if (url.Contains("address-change", StringComparison.Ordinal))
            {
                postedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"isValidAddress":1,"successful":1}""",
                        Encoding.UTF8,
                        "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var http = new HttpClient(handler);
        using var client = new AmazonClient(
            UnitedStatesMarketplace.Instance,
            new AmazonOptions { Transport = AmazonTransport.Http, GlowStepDelayMs = 0 },
            http);

        var result = await client.SetDeliveryLocationAsync(
            "https://www.amazon.com/dp/B0H4SBSYBS",
            "10001",
            "US");

        Assert.True(result);
        Assert.NotNull(postedBody);
        using var document = JsonDocument.Parse(postedBody!);
        var root = document.RootElement;
        Assert.Equal("LOCATION_INPUT", root.GetProperty("locationType").GetString());
        Assert.Equal("10001", root.GetProperty("zipCode").GetString());
        Assert.Equal("exports-gateway-desktop", root.GetProperty("pageType").GetString());
        Assert.Equal("generic", root.GetProperty("storeContext").GetString());
        Assert.False(root.TryGetProperty("countryCode", out _));
    }

    private static HttpResponseMessage JsonHtml(string html) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html"),
        };

    [Fact]
    public async Task Warmup_waf_interstitial_does_not_abort_search()
    {
        var requests = new List<string>();
        const string waf = """
            <html>
              <script>window.awsWafCookieDomainList = []; window.gokuProps = {};</script>
              <script src="https://example/challenge.js"></script>
            </html>
            """;

        var handler = new StubHandler(request =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            if (request.RequestUri!.AbsolutePath == "/")
            {
                return new HttpResponseMessage(HttpStatusCode.Accepted)
                {
                    Content = new StringContent(waf),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>search result</html>"),
            };
        });

        using var http = new HttpClient(handler);
        using var client = new AmazonClient(ItalyMarketplace.Instance, new AmazonOptions(), http);

        var html = await client.SearchAsync("asus");

        Assert.Equal("<html>search result</html>", html);
        Assert.Equal(2, requests.Count);
        Assert.Equal("https://www.amazon.it/", requests[0]);
        Assert.Equal("https://www.amazon.it/s?k=asus", requests[1]);
    }

    [Fact]
    public async Task Warmup_http_503_does_not_abort_search()
    {
        var requests = new List<string>();
        var handler = new StubHandler(request =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            if (request.RequestUri!.AbsolutePath == "/")
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>search result</html>"),
            };
        });

        using var http = new HttpClient(handler);
        using var client = new AmazonClient(ItalyMarketplace.Instance, new AmazonOptions(), http);

        var html = await client.SearchAsync("asus");

        Assert.Equal("<html>search result</html>", html);
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task Http_404_raises_not_found_instead_of_blocked()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html>home</html>"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("<html>api-services-support@amazon.com</html>"),
            };
        });

        using var http = new HttpClient(handler);
        using var client = new AmazonClient(ItalyMarketplace.Instance, new AmazonOptions(), http);

        var exception = await Assert.ThrowsAsync<AmazonNotFoundException>(
            () => client.DetailAsync("B09CD32DNH"));
        Assert.Contains("HTTP 404", exception.Message);
    }

    [Fact]
    public async Task Http_403_raises_amazon_blocked_exception()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var http = new HttpClient(handler);
        using var client = new AmazonClient(ItalyMarketplace.Instance, new AmazonOptions(), http);

        var exception = await Assert.ThrowsAsync<AmazonBlockedException>(() => client.SearchAsync("x"));
        Assert.Contains("HTTP 403", exception.Message);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }
}
