using System.ComponentModel;
using AmazonScraper.Core.Searching;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Commands;

public class CommonSettings : CommandSettings
{
    [CommandOption("--timeout <SECONDS>")]
    [Description("HTTP timeout in seconds (default: 30 or the saved setting)")]
    public double? Timeout { get; set; }

    [CommandOption("--domain <DOMAIN>")]
    [Description("Amazon marketplace: it (default), de, fr, uk, com/us, jp, es")]
    public string? Domain { get; set; } =
        Environment.GetEnvironmentVariable("AMZ_DOMAIN");

    [CommandOption("--width <COLUMNS>")]
    [Description("Console width for tables (default 200)")]
    [DefaultValue(200)]
    public int Width { get; set; } = 200;

    [CommandOption("--transport <TRANSPORT>")]
    [Description("HTTP transport: curl (default), http; for .com the default is http")]
    public string? Transport { get; set; }

    [CommandOption("--browser <PROFILE>")]
    [Description("curl-impersonate TLS profile (default depends on the domain; e.g. Firefox147 for .co.uk)")]
    public string? Browser { get; set; }

    [CommandOption("--user-agent <UA>")]
    [Description("Explicit HTTP User-Agent (default: iPad Safari)")]
    public string? UserAgent { get; set; }

    [CommandOption("--browser-headers")]
    [Description("Use the full browser headers from the curl-impersonate profile")]
    public bool BrowserHeaders { get; set; }

    [CommandOption("--cookie <HEADER>")]
    [Description("Explicit HTTP cookies, for example aws-waf-token=...; session-id=...")]
    public string? Cookie { get; set; }

    [CommandOption("--save-html <FILE>")]
    [Description("Save the received HTML to a file (debug)")]
    public string? SaveHtml { get; set; }

    public override ValidationResult Validate()
    {
        if (Timeout is <= 0)
        {
            return ValidationResult.Error("--timeout must be greater than zero.");
        }
        if (Width < 40)
        {
            return ValidationResult.Error("--width must be at least 40.");
        }
        if (Transport is not (null or "" or "curl" or "http"))
        {
            return ValidationResult.Error("--transport must be 'curl' or 'http'.");
        }
        return AmazonOptionsFactory.ValidateBrowser(Browser);
    }
}

public sealed class SearchSettings : CommonSettings
{
    [CommandArgument(0, "<QUERY>")]
    [Description("Text to search on Amazon")]
    public string Query { get; set; } = string.Empty;

    [CommandOption("--json")]
    [Description("JSON output")]
    public bool Json { get; set; }

    [CommandOption("--agent")]
    [Description("Mark the operation as agent-generated in the shared history")]
    public bool Agent { get; set; }

    [CommandOption("--tk-opt")]
    [Description("Tab-separated output")]
    public bool TkOpt { get; set; }

    [CommandOption("--html <FILE>")]
    [Description("Read HTML from a file instead of using the network (debug/test)")]
    public string? HtmlFile { get; set; }

    [CommandOption("--delivery")]
    [Description("Show the estimated delivery from search cards")]
    public bool Delivery { get; set; }

    [CommandOption("--no-ads")]
    [Description("Exclude sponsored results")]
    public bool NoAds { get; set; }

    [CommandOption("--enrich")]
    [Description("Open the detail page for products without a strikethrough price or discount")]
    public bool Enrich { get; set; }

    [CommandOption("--enrich-delay <MS>")]
    [Description("Delay between enrich requests in milliseconds (default 1500)")]
    [DefaultValue(1500)]
    public int EnrichDelay { get; set; } = 1500;

    [CommandOption("--deals")]
    [Description("Show only products with deals/discounts using the Amazon filter")]
    public bool Deals { get; set; }

    [CommandOption("--deal-type <TYPE>")]
    [Description("With --deals: all (default), today, or coupons")]
    public string? DealType { get; set; }

    [CommandOption("--page <SPEC>")]
    [Description("Pages to fetch: 2, 1,3,5, 3-5, 1,3-5,8")]
    public string? Page { get; set; }

    [CommandOption("--pages <N>")]
    [Description("Fetch the first N pages (1-20)")]
    public int? Pages { get; set; }

    [CommandOption("--page-delay <MS>")]
    [Description("Delay between pages in milliseconds (default 2000)")]
    [DefaultValue(2000)]
    public int PageDelay { get; set; } = 2000;

    [CommandOption("--no-fallback")]
    [Description("Do not retry with curl when the HTTP transport is blocked")]
    public bool NoTransportFallback { get; set; }

    public override ValidationResult Validate()
    {
        var baseResult = base.Validate();
        if (!baseResult.Successful)
        {
            return baseResult;
        }

        if (string.IsNullOrWhiteSpace(Query))
        {
            return ValidationResult.Error("The query cannot be empty.");
        }

        if (Page is not null && Pages is not null)
        {
            return ValidationResult.Error("Use --page or --pages, not both.");
        }

        if (Page is not null
            && !PageSelection.TryParse(Page, out _, out var pageError))
        {
            return ValidationResult.Error(pageError);
        }

        if (Pages is { } count && count is < PageSelection.MinPage or > PageSelection.MaxPage)
        {
            return ValidationResult.Error(
                $"--pages must be between {PageSelection.MinPage} and {PageSelection.MaxPage}.");
        }

        if (PageDelay is < 0 or > 60_000)
        {
            return ValidationResult.Error("--page-delay must be between 0 and 60000 ms.");
        }

        if (EnrichDelay is < 0 or > 60_000)
        {
            return ValidationResult.Error("--enrich-delay must be between 0 and 60000 ms.");
        }

        if (Enrich && HtmlFile is not null)
        {
            return ValidationResult.Error(
                "--enrich is not compatible with --html: it requires fetching the detail page from the network.");
        }

        if (!string.IsNullOrWhiteSpace(DealType))
        {
            if (!Deals)
            {
                return ValidationResult.Error(
                    "--deal-type requires --deals.");
            }

            if (!DealFilterCatalog.TryParseKind(DealType, out _))
            {
                return ValidationResult.Error(
                    "--deal-type must be all, today, or coupons.");
            }
        }

        if (Deals && HtmlFile is not null)
        {
            return ValidationResult.Error(
                "--deals is not compatible with --html: the deal filter " +
                "is applied to the network request.");
        }

        if (HtmlFile is not null)
        {
            if (Page is not null
                && PageSelection.TryParse(Page, out var htmlSelection, out _)
                && htmlSelection.Pages.Count > 1)
            {
                return ValidationResult.Error("With --html you can only read one page.");
            }

            if (Pages is > 1)
            {
                return ValidationResult.Error("With --html you can only read one page; use --pages 1.");
            }
        }

        return ValidationResult.Success();
    }
}

public sealed class DealsSettings : CommonSettings
{
    [CommandOption("--deal-type <TYPE>")]
    [Description("Deal type: all (default), today, or coupons")]
    [DefaultValue("all")]
    public string DealType { get; set; } = "all";

    [CommandOption("--category <NAME>")]
    [Description("Filter by Amazon department/category (e.g. Electronics)")]
    public string? Category { get; set; }

    [CommandOption("--json")]
    [Description("JSON output")]
    public bool Json { get; set; }

    [CommandOption("--tk-opt")]
    [Description("Tab-separated output")]
    public bool TkOpt { get; set; }

    [CommandOption("--delivery")]
    [Description("Show the estimated delivery from the cards")]
    public bool Delivery { get; set; }

    [CommandOption("--no-ads")]
    [Description("Exclude sponsored results")]
    public bool NoAds { get; set; }

    [CommandOption("--enrich")]
    [Description("Open the detail page for products without a strikethrough price or discount")]
    public bool Enrich { get; set; }

    [CommandOption("--enrich-delay <MS>")]
    [Description("Delay between enrich requests in milliseconds (default 1500)")]
    [DefaultValue(1500)]
    public int EnrichDelay { get; set; } = 1500;

    [CommandOption("--page <SPEC>")]
    [Description("Pages to fetch: 2, 1,3,5, 3-5, 1,3-5,8")]
    public string? Page { get; set; }

    [CommandOption("--pages <N>")]
    [Description("Fetch the first N pages (1-20)")]
    public int? Pages { get; set; }

    [CommandOption("--page-delay <MS>")]
    [Description("Delay between pages in milliseconds (default 2000)")]
    [DefaultValue(2000)]
    public int PageDelay { get; set; } = 2000;

    [CommandOption("--limit <N>")]
    [Description("Maximum number of deals to show (default: all)")]
    public int? Limit { get; set; }

    [CommandOption("--html <FILE>")]
    [Description("Read the deals HTML from a file instead of using the network")]
    public string? HtmlFile { get; set; }

    public override ValidationResult Validate()
    {
        var baseResult = base.Validate();
        if (!baseResult.Successful)
        {
            return baseResult;
        }

        if (Page is not null && Pages is not null)
        {
            return ValidationResult.Error("Use --page or --pages, not both.");
        }

        if (Page is not null
            && !PageSelection.TryParse(Page, out _, out var pageError))
        {
            return ValidationResult.Error(pageError);
        }

        if (Pages is { } count && count is < PageSelection.MinPage or > PageSelection.MaxPage)
        {
            return ValidationResult.Error(
                $"--pages must be between {PageSelection.MinPage} and {PageSelection.MaxPage}.");
        }

        if (PageDelay is < 0 or > 60_000)
        {
            return ValidationResult.Error("--page-delay must be between 0 and 60000 ms.");
        }

        if (EnrichDelay is < 0 or > 60_000)
        {
            return ValidationResult.Error("--enrich-delay must be between 0 and 60000 ms.");
        }

        if (Enrich && HtmlFile is not null)
        {
            return ValidationResult.Error(
                "--enrich is not compatible with --html: it requires fetching the detail page from the network.");
        }

        if (Limit is { } limit && limit is < 1 or > 1000)
        {
            return ValidationResult.Error("--limit must be between 1 and 1000.");
        }

        if (!DealFilterCatalog.TryParseKind(DealType, out _))
        {
            return ValidationResult.Error(
                "--deal-type must be all, today, or coupons.");
        }

        if (Category is not null)
        {
            if (string.IsNullOrWhiteSpace(Category))
            {
                return ValidationResult.Error("--category requires a category name.");
            }
            if (HtmlFile is not null)
            {
                return ValidationResult.Error(
                    "--category requires a network request; do not use it with --html.");
            }
        }

        if (HtmlFile is not null)
        {
            if (Page is not null
                && PageSelection.TryParse(Page, out var htmlSelection, out _)
                && htmlSelection.Pages.Count > 1)
            {
                return ValidationResult.Error("With --html you can only read one page.");
            }

            if (Pages is > 1)
            {
                return ValidationResult.Error("With --html you can only read one page; use --pages 1.");
            }
        }

        return ValidationResult.Success();
    }
}

public sealed class DetailsSettings : CommonSettings
{
    [CommandArgument(0, "<ASINS>")]
    [Description("One or more ASINs (10 characters)")]
    public string[] Asins { get; set; } = [];

    [CommandOption("--json")]
    [Description("JSON output")]
    public bool Json { get; set; }

    [CommandOption("--agent")]
    [Description("Mark the operation as agent-generated in the shared history")]
    public bool Agent { get; set; }

    [CommandOption("--tk-opt")]
    [Description("Tab-separated output")]
    public bool TkOpt { get; set; }

    [CommandOption("--compare")]
    [Description("Compare products: one row per ASIN")]
    public bool Compare { get; set; }

    [CommandOption("--variants")]
    [Description("Show available variants/sizes")]
    public bool Variants { get; set; }

    [CommandOption("--variant-prices")]
    [Description("Fetch the price and availability of each variant")]
    public bool VariantPrices { get; set; }

    [CommandOption("--variant-delay <MS>")]
    [Description("Delay between variant requests in milliseconds (default 1500)")]
    [DefaultValue(1500)]
    public int VariantDelay { get; set; } = 1500;

    [CommandOption("--rating")]
    [Description("Add the numeric average, count, and star histogram")]
    public bool Rating { get; set; }

    [CommandOption("--reviews")]
    [Description("Add the reviews available on the detail page")]
    public bool Reviews { get; set; }

    [CommandOption("--review-limit <N>")]
    [Description("Maximum number of reviews to show (1-50; default: all)")]
    public int? ReviewLimit { get; set; }

    [CommandOption("--review-images")]
    [Description("Include the URLs of images attached to reviews")]
    public bool ReviewImages { get; set; }

    [CommandOption("--html <FILE>")]
    [Description("Read HTML from a file instead of using the network (single ASIN)")]
    public string? HtmlFile { get; set; }

    [CommandOption("--country <ISO>")]
    [Description("Two-letter ISO delivery country (default: marketplace country)")]
    public string? Country { get; set; }

    [CommandOption("--cap <POSTAL>")]
    [Description("Delivery postal code (e.g. 20121, 75001, E1 6AN)")]
    public string? Cap { get; set; }

    [CommandOption("--no-buying-options")]
    [Description("Do not load buying options (no additional AOD request)")]
    public bool NoBuyingOptions { get; set; }

    [CommandOption("--options-delay <MS>")]
    [Description("Delay before each AOD request (default 1500)")]
    [DefaultValue(1500)]
    public int OptionsDelay { get; set; } = 1500;

    [CommandOption("--all-options")]
    [Description("Load all AOD pages (max 10 pages)")]
    public bool AllOptions { get; set; }

    [CommandOption("--options-pages <N>")]
    [Description("Maximum number of AOD pages to load (1-10)")]
    public int? OptionsPages { get; set; }

    [CommandOption("--smid <SELLER_ID>")]
    [Description("Analyze the offer from a specific seller (Seller ID / smid)")]
    public string? SellerId { get; set; }

    [CommandOption("--save-aod-html <FILE>")]
    [Description("Save the AOD response HTML to a file (debug)")]
    public string? SaveAodHtml { get; set; }

    public override ValidationResult Validate()
    {
        var baseResult = base.Validate();
        if (!baseResult.Successful)
        {
            return baseResult;
        }
        if (Asins.Length == 0)
        {
            return ValidationResult.Error("At least one ASIN is required.");
        }
        if (HtmlFile is not null && Asins.Length != 1)
        {
            return ValidationResult.Error("--html requires exactly one ASIN.");
        }
        if (Country is not null && !PostalCodeValidator.IsCountryCode(Country))
        {
            return ValidationResult.Error(
                "--country must be a two-letter ISO code (e.g. IT, FR, GB, ES).");
        }
        if ((Cap is not null || Country is not null) && HtmlFile is not null)
        {
            return ValidationResult.Error(
                "--country/--cap require a network request; do not use them with --html.");
        }
        if (OptionsDelay is < 0 or > 60_000)
        {
            return ValidationResult.Error("--options-delay must be between 0 and 60000 ms.");
        }
        if (VariantDelay is < 0 or > 60_000)
        {
            return ValidationResult.Error("--variant-delay must be between 0 and 60000 ms.");
        }
        if (VariantPrices && HtmlFile is not null)
        {
            return ValidationResult.Error(
                "--variant-prices requires a network request; do not use it with --html.");
        }
        if ((Rating || Reviews) && Compare)
        {
            return ValidationResult.Error(
                "--rating/--reviews are not compatible with --compare.");
        }
        if (ReviewImages && !Reviews)
        {
            return ValidationResult.Error("--review-images requires --reviews.");
        }
        if (ReviewLimit is not null && !Reviews)
        {
            return ValidationResult.Error("--review-limit requires --reviews.");
        }
        if (ReviewLimit is < 1 or > 50)
        {
            return ValidationResult.Error("--review-limit must be between 1 and 50.");
        }
        if (AllOptions && OptionsPages is not null)
        {
            return ValidationResult.Error("Use --all-options or --options-pages, not both.");
        }
        if (OptionsPages is < 1 or > 10)
        {
            return ValidationResult.Error("--options-pages must be between 1 and 10.");
        }
        if ((AllOptions || OptionsPages is not null) && HtmlFile is not null)
        {
            return ValidationResult.Error("--all-options/--options-pages require a network request; do not use them with --html.");
        }
        if ((AllOptions || OptionsPages is not null) && NoBuyingOptions)
        {
            return ValidationResult.Error("--all-options/--options-pages are not compatible with --no-buying-options.");
        }
        if (SellerId is not null)
        {
            if (Asins.Length != 1)
            {
                return ValidationResult.Error("--smid requires exactly one ASIN.");
            }
            if (HtmlFile is not null)
            {
                return ValidationResult.Error("--smid requires a network request; do not use it with --html.");
            }
            if (Compare)
            {
                return ValidationResult.Error("--smid is not compatible with --compare.");
            }
            if (string.IsNullOrWhiteSpace(SellerId)
                || SellerId.Any(character => !char.IsLetterOrDigit(character)))
            {
                return ValidationResult.Error("--smid must be an alphanumeric Seller ID.");
            }
        }
        if (SaveAodHtml is not null && Asins.Length != 1)
        {
            return ValidationResult.Error("--save-aod-html requires exactly one ASIN.");
        }
        return ValidationResult.Success();
    }
}

public sealed class SelfTestSettings : CommandSettings
{
    [CommandOption("--allow-http")]
    [Description("Do not fail if the curl transport is unavailable")]
    public bool AllowHttp { get; set; }
}
