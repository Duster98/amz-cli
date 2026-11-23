using System.Text.Json;
using System.Text.Json.Nodes;
using AmazonScraper.Core.Formatting;
using AmazonScraper.Core.Http;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Models;
using AmazonScraper.Core.Parsing;
using AmazonScraper.Core.Searching;
using AmazonScraper.Cli.History;
using AmazonScraper.Cli.Library;
using AmazonScraper.Cli.Rendering;
using AmazonScraper.Cli.Services;
using AmazonScraper.Cli.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Commands;

public sealed class DetailsCommand : AsyncCommand<DetailsSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DetailsSettings settings, CancellationToken cancellationToken)
    {
        var mode = OutputModeSelector.Pick(settings.Json, settings.TkOpt);
        IMarketplaceProfile marketplace;
        try
        {
            marketplace = MarketplaceRegistry.Resolve(settings.Domain);
        }
        catch (MarketplaceException ex)
        {
            CliConsole.Error(ex.Message);
            return 1;
        }

        var savedHttp = HttpSettingsStore.Default.Load();
        var savedTransportExplicit = savedHttp.Transport is "curl" or "http";
        var forceCurlForDelivery =
            (settings.Country is not null || settings.Cap is not null)
            && marketplace.Id == "com"
            && string.IsNullOrWhiteSpace(settings.Transport)
            && !savedTransportExplicit;
        var options = AmazonOptionsFactory.Create(
            settings,
            marketplace,
            forceCurlForDelivery ? AmazonTransport.Curl : null,
            saved: savedHttp);
        var showVariants = settings.Variants || settings.VariantPrices;

        var parser = new DetailParser(marketplace);
        var details = new List<ProductDetail>();
        var jsonItems = new List<JsonNode?>();
        var collect = mode == OutputMode.Json || settings.Compare;
        var historyStore = new HistoryStore();
        var productCache = new ProductCacheStore();
        var libraryStore = new SavedLibraryStore();

        AmazonClient? client = null;
        string? deliveryCountry = null;
        string? deliveryPostalCode = null;
        try
        {
            if (settings.HtmlFile is null)
            {
                client = new AmazonClient(marketplace, options);
            }

            if (settings.Cap is not null || settings.Country is not null)
            {
                if (client is null)
                {
                    CliConsole.Error(
                        "--country/--cap require a network request; do not use them with --html.");
                    return 1;
                }

                DeliveryLocation location;
                try
                {
                    location = DeliveryLocation.Normalize(
                        settings.Country,
                        settings.Cap,
                        marketplace)
                        ?? throw new ArgumentException(
                            "Specify a delivery country or postal code.");
                }
                catch (ArgumentException ex)
                {
                    CliConsole.Error(ex.Message);
                    return 1;
                }

                deliveryCountry = location.CountryCode;
                deliveryPostalCode = location.PostalCode;

                try
                {
                    var seedUrl = settings.SellerId is not null
                        ? marketplace.BuyingOptionDetailUrl(settings.Asins[0], settings.SellerId)
                        : marketplace.DetailUrl(settings.Asins[0]);
                    var locationSet = await client
                        .SetDeliveryLocationAsync(
                            seedUrl,
                            location.PostalCode,
                            location.CountryCode,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (!locationSet)
                    {
                        var destination = location.CountryOnly
                            ? location.CountryCode
                            : $"{location.PostalCode} ({location.CountryCode})";
                        CliConsole.Error($"Could not set delivery to {destination}.");
                        return 1;
                    }
                }
                catch (Exception ex)
                {
                    CliConsole.Error($"Could not set the delivery location: {ex.Message}");
                    return 1;
                }
            }

            foreach (var asin in settings.Asins)
            {
                if (settings.Asins.Length > 1 && !collect && mode == OutputMode.Table)
                {
                    CliConsole.Stderr.WriteLine();
                    CliConsole.Stderr.MarkupLine($"[bold]─── {Markup.Escape(asin)} ───[/]");
                }

                try
                {
                    var html = settings.HtmlFile is not null
                        ? await File.ReadAllTextAsync(settings.HtmlFile, cancellationToken).ConfigureAwait(false)
                        : settings.SellerId is not null
                            ? await client!.SellerDetailAsync(asin, settings.SellerId, cancellationToken)
                                .ConfigureAwait(false)
                            : await client!.DetailAsync(asin, cancellationToken).ConfigureAwait(false);

                    var detail = parser.Parse(html, asin, deliveryCountry, deliveryPostalCode);
                    if (string.IsNullOrWhiteSpace(detail.Title)
                        && string.IsNullOrWhiteSpace(detail.Price)
                        && detail.Images.Count == 0
                        && detail.Description.Count == 0
                        && detail.Specs.Count == 0)
                    {
                        throw new InvalidOperationException(
                            "Detail page with no product content " +
                            "(possible block, redirect, or unsupported layout).");
                    }

                    if (settings.SellerId is not null)
                    {
                        detail.Url = marketplace.BuyingOptionDetailUrl(asin, settings.SellerId);
                    }

                    if (settings.Rating)
                    {
                        detail.RatingDetails = CustomerReviewsParser.ParseRating(
                            html,
                            marketplace);
                    }

                    if (settings.Reviews)
                    {
                        detail.TopReviews = CustomerReviewsParser.ParseReviews(
                            html,
                            marketplace,
                            settings.ReviewLimit,
                            settings.ReviewImages);
                        if (detail.TopReviews is null)
                        {
                            CliConsole.Stderr.MarkupLine(
                                $"[yellow]Reviews unavailable on the page for " +
                                $"{Markup.Escape(asin)} (Amazon may require a login).[/]");
                        }
                    }

                    if (settings.VariantPrices
                        && detail.Variants is { Items.Count: > 0 }
                        && client is not null)
                    {
                        var variantWarnings = await ProductVariantEnricher.EnrichAsync(
                                detail,
                                marketplace,
                                client,
                                settings.VariantDelay,
                                cancellationToken)
                            .ConfigureAwait(false);
                        foreach (var warning in variantWarnings)
                        {
                            CliConsole.Stderr.MarkupLine(
                                $"[yellow]{Markup.Escape(warning)}[/]");
                        }
                    }

                    if (client is not null
                        && !settings.NoBuyingOptions
                        && (mode == OutputMode.Json || !settings.Compare)
                        && (detail.BuyingOptions is not null
                            || string.IsNullOrEmpty(detail.Price)
                            || AmazonPatterns.BuyingOptionsLink.IsMatch(html)))
                    {
                        await LoadBuyingOptionsAsync(
                            client, marketplace, settings, detail, asin, cancellationToken).ConfigureAwait(false);
                    }

                    if (settings.SaveHtml is not null)
                    {
                        await File.WriteAllTextAsync(settings.SaveHtml, html, cancellationToken).ConfigureAwait(false);
                    }
                    details.Add(detail);
                    RecordHistory(
                        historyStore,
                        productCache,
                        libraryStore,
                        settings,
                        marketplace,
                        detail,
                        asin,
                        deliveryCountry,
                        deliveryPostalCode,
                        liveFetch: settings.HtmlFile is null);

                    if (mode == OutputMode.Json)
                    {
                        jsonItems.Add(JsonSerializer.SerializeToNode(detail, JsonFormatter.Options));
                    }
                    else if (!collect)
                    {
                        if (mode == OutputMode.Tsv)
                        {
                            Console.Out.WriteLine(TsvFormatter.Detail(
                                detail,
                                showVariants,
                                settings.Rating,
                                settings.Reviews));
                        }
                        else
                        {
                            DetailTableRenderer.Render(
                                CliConsole.Stdout(settings.Width),
                                detail,
                                showVariants,
                                settings.Rating,
                                settings.Reviews);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (mode == OutputMode.Json)
                    {
                        jsonItems.Add(new JsonObject
                        {
                            ["asin"] = asin,
                            ["error"] = ex.Message,
                        });
                    }
                    else
                    {
                        CliConsole.Error($"Error: {asin} — {ex.Message}");
                    }

                    if (ex is AmazonBlockedException)
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            client?.Dispose();
        }

        if (mode == OutputMode.Json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(jsonItems, JsonFormatter.Options));
        }
        else if (settings.Compare && details.Count > 0)
        {
            if (mode == OutputMode.Tsv)
            {
                Console.Out.WriteLine(TsvFormatter.Compare(details));
            }
            else
            {
                CompareTableRenderer.Render(CliConsole.Stdout(settings.Width), details, marketplace.Format);
            }
        }

        return 0;
    }

    private const int MaxOptionsPages = 10;
    private const int OptionsPageSize = 10;

    private static async Task LoadBuyingOptionsAsync(
        AmazonClient client,
        IMarketplaceProfile marketplace,
        DetailsSettings settings,
        ProductDetail detail,
        string asin,
        CancellationToken cancellationToken)
    {
        try
        {
            if (settings.OptionsDelay > 0)
            {
                await Task.Delay(settings.OptionsDelay, cancellationToken).ConfigureAwait(false);
            }

            var aodHtml = await client
                .FetchBuyingOptionsPageAsync(asin, 1, cancellationToken)
                .ConfigureAwait(false);
            if (settings.SaveAodHtml is not null)
            {
                await File.WriteAllTextAsync(
                    settings.SaveAodHtml,
                    aodHtml,
                    cancellationToken).ConfigureAwait(false);
            }

            var fromAod = BuyingOptionsParser.Parse(aodHtml, marketplace, asin);
            if (fromAod is not null)
            {
                detail.BuyingOptions = BuyingOptionsSummary.Combine(
                    detail.BuyingOptions,
                    fromAod);
            }

            if (detail.BuyingOptions is null)
            {
                return;
            }

            var totalCount = detail.BuyingOptions.Count;
            var requiredPages = totalCount > 0
                ? (int)Math.Ceiling(totalCount / (double)OptionsPageSize)
                : 0;
            var pages = ResolveOptionsPages(settings, totalCount);

            if (settings.AllOptions && requiredPages > MaxOptionsPages)
            {
                CliConsole.Stderr.MarkupLine(
                    $"[yellow]Limit of {MaxOptionsPages} AOD pages reached for " +
                    $"{Markup.Escape(asin)}: partial options.[/]");
            }

            for (var page = 2; page <= pages; page++)
            {
                try
                {
                    if (settings.OptionsDelay > 0)
                    {
                        await Task.Delay(settings.OptionsDelay, cancellationToken).ConfigureAwait(false);
                    }

                    var pageHtml = await client
                        .FetchBuyingOptionsPageAsync(asin, page, cancellationToken)
                        .ConfigureAwait(false);
                    if (settings.SaveAodHtml is not null)
                    {
                        await File.WriteAllTextAsync(
                            BuildAodPagePath(settings.SaveAodHtml, page),
                            pageHtml,
                            cancellationToken).ConfigureAwait(false);
                    }

                    var pageSummary = BuyingOptionsParser.Parse(pageHtml, marketplace, asin);
                    if (pageSummary is null || pageSummary.Options.Count == 0)
                    {
                        break;
                    }

                    detail.BuyingOptions.AddOptions(pageSummary.Options);
                    if (totalCount > 0
                        && detail.BuyingOptions.Options.Count >= totalCount)
                    {
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    CliConsole.Stderr.MarkupLine(
                        $"[yellow]AOD page {page} unavailable for " +
                        $"{Markup.Escape(asin)}: {Markup.Escape(ex.Message)}[/]");
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The product detail is still valid: AOD is an optional extra and
            // can be blocked while the product page is not.
            CliConsole.Stderr.MarkupLine(
                $"[yellow]Buying options unavailable for " +
                $"{Markup.Escape(asin)}: {Markup.Escape(ex.Message)}[/]");
        }
    }

    private static void RecordHistory(
        HistoryStore historyStore,
        ProductCacheStore productCache,
        SavedLibraryStore libraryStore,
        DetailsSettings settings,
        IMarketplaceProfile marketplace,
        ProductDetail detail,
        string asin,
        string? deliveryCountry,
        string? deliveryPostalCode,
        bool liveFetch)
    {
        try
        {
            var priceAmount = marketplace.Format.TryParsePrice(
                detail.Price,
                out var parsed)
                ? parsed
                : (decimal?)null;
            _ = historyStore.AddProduct(new HistoryProductInput(
                string.IsNullOrWhiteSpace(detail.Asin)
                    ? "unknown"
                    : detail.Asin,
                marketplace.Id,
                new HistoryProductSnapshot
                {
                    Title = detail.Title,
                    Image = detail.Image,
                    Price = detail.Price,
                    PriceAmount = priceAmount,
                    Currency = marketplace.CurrencyCode,
                    OriginalPrice = detail.OriginalPrice,
                    Discount = detail.Discount,
                    Rating = detail.Rating,
                    Reviews = detail.Reviews,
                    Availability = detail.Availability,
                    Url = detail.Url,
                },
                settings.Agent ? HistorySource.Agent : HistorySource.Cli));
        }
        catch (HistoryException)
        {
            // History is best effort and must not break details output.
        }

        try
        {
            var result = new ProductDetailsResult(
                marketplace.Id,
                marketplace.DisplayName,
                marketplace.CurrencyCode,
                marketplace.CountryCode,
                detail,
                [],
                Cached: false,
                DateTimeOffset.UtcNow,
                TimeSpan.Zero,
                Stale: false);
            SavedProduct? savedProduct = null;
            try
            {
                savedProduct = SavedProductResolver.FromDetails(result);
            }
            catch (SavedLibraryException)
            {
                // A partial product page can still be cached and shown.
            }
            if (liveFetch && savedProduct is not null)
            {
                try
                {
                    libraryStore.TrySyncProductDetails(
                        savedProduct,
                        result.FetchedAt);
                }
                catch (SavedLibraryException)
                {
                    // Library synchronization is best effort.
                }
            }
            productCache.Save(
                ProductCacheStore.Key(marketplace.Id, asin),
                AmazonScraper.Cli.Web.WebHostFactory.ProductResultNode(result),
                new ProductCacheContext
                {
                    Rating = settings.Rating,
                    Reviews = settings.Reviews,
                    Options = !settings.NoBuyingOptions,
                    Country = deliveryCountry,
                    PostalCode = deliveryPostalCode,
                },
                savedProduct);
        }
        catch (ProductCacheException)
        {
            // Product caching is best effort and must not break details output.
        }
    }

    private static int ResolveOptionsPages(DetailsSettings settings, int count)
    {
        if (settings.OptionsPages is { } requested)
        {
            return Math.Clamp(requested, 1, MaxOptionsPages);
        }

        if (!settings.AllOptions || count <= 0)
        {
            return 1;
        }

        return Math.Min(
            MaxOptionsPages,
            (int)Math.Ceiling(count / (double)OptionsPageSize));
    }

    private static string BuildAodPagePath(string path, int page)
    {
        if (page <= 1)
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path);
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        return Path.Combine(directory ?? string.Empty, $"{name}-p{page}{extension}");
    }
}
