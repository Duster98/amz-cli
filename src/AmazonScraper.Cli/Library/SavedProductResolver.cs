using AmazonScraper.Cli.Services;
using AmazonScraper.Cli.Storage;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Library;

public sealed class SavedProductResolver
{
    private readonly ProductDetailsService _details;

    public SavedProductResolver(ProductDetailsService? details = null)
    {
        _details = details ?? new ProductDetailsService();
    }

    public async Task<SavedProduct> ResolveAsync(
        MonitorTarget target,
        double timeoutSeconds,
        HttpSettings? httpSettings = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _details
            .GetAsync(
                new ProductDetailsRequest(
                    target.Asin,
                    target.Domain,
                    IncludeBuyingOptions: false,
                    TimeoutSeconds: timeoutSeconds,
                    Http: httpSettings),
                cancellationToken)
            .ConfigureAwait(false);
        return FromDetails(result);
    }

    public static SavedProduct FromDetails(ProductDetailsResult result)
    {
        var profile = MarketplaceRegistry.Resolve(result.MarketplaceId);
        var detail = result.Product;
        var priceText = !string.IsNullOrWhiteSpace(detail.Price)
            ? detail.Price
            : detail.BuyingOptions?.LowestPrice ?? string.Empty;
        var priceAmount = profile.Format.TryParsePrice(
            priceText,
            out var parsedPrice)
            ? parsedPrice
            : (decimal?)null;
        if (string.IsNullOrWhiteSpace(detail.Title)
            && string.IsNullOrWhiteSpace(detail.Image))
        {
            throw new SavedLibraryException(
                $"The product page for {result.MarketplaceId}:" +
                $"{detail.Asin} did not contain product data.");
        }

        var now = DateTimeOffset.UtcNow;
        return new SavedProduct
        {
            Id = $"{result.MarketplaceId}:{detail.Asin}",
            Asin = detail.Asin,
            Domain = result.MarketplaceId,
            SavedAt = now,
            UpdatedAt = now,
            LastCheckedAt = result.FetchedAt == default
                ? now
                : result.FetchedAt,
            PriceAtSave = priceAmount,
            PriceLast = priceAmount,
            Currency = result.Currency,
            Snapshot = new SavedSnapshot
            {
                Title = detail.Title,
                Image = detail.Image,
                Price = priceText,
                PriceAmount = priceAmount,
                Currency = result.Currency,
                OriginalPrice = detail.OriginalPrice,
                Discount = detail.Discount,
                Rating = detail.Rating,
                Reviews = detail.Reviews,
                DeliveryText = detail.Delivery?.Time ?? string.Empty,
                Availability = detail.Availability,
                Url = detail.Url,
                Charger = detail.Charger,
            },
        };
    }
}
