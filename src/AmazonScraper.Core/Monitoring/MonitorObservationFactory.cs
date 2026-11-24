using System.Globalization;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Models;

namespace AmazonScraper.Core.Monitoring;

public static class MonitorObservationFactory
{
    public static MonitorItemObservation FromDetail(
        ProductDetail detail,
        MonitorTargetPlan plan,
        string runId,
        int cycle,
        DateTimeOffset observedAt)
    {
        var profile = MarketplaceRegistry.Resolve(plan.Target.Domain);
        var priceAmount = ParsePrice(profile, detail.Price);
        var originalAmount = ParsePrice(profile, detail.OriginalPrice);
        var primeAmount = detail.Prime is { } prime
            ? ParsePrice(profile, prime.Price)
            : null;

        return new MonitorItemObservation
        {
            ObservedAt = FormatTimestamp(observedAt),
            RunId = runId,
            Cycle = cycle,
            Domain = plan.Target.Domain,
            Asin = plan.Target.Asin,
            GroupId = plan.GroupId,
            Label = plan.Label,
            Url = string.IsNullOrWhiteSpace(detail.Url)
                ? profile.DetailUrl(plan.Target.Asin)
                : detail.Url,
            Title = detail.Title,
            Price = detail.Price,
            PriceAmount = priceAmount,
            Currency = profile.CurrencyCode,
            OriginalPrice = detail.OriginalPrice,
            OriginalPriceAmount = originalAmount,
            Discount = detail.Discount,
            PrimePrice = detail.Prime?.Price ?? string.Empty,
            PrimePriceAmount = primeAmount,
            PrimeOnly = detail.Prime?.Only ?? false,
            Coupon = detail.Coupon,
            Deal = detail.Deal,
            DealId = detail.DealId,
            DealEndsText = detail.DealEndsText,
            DealEndsOn = detail.DealEndsOn,
            DealEndsInDays = detail.DealEndsInDays,
            DealHasCountdown = detail.DealHasCountdown,
            Availability = detail.Availability,
            Seller = detail.Seller,
            ShippedBy = detail.ShippedBy,
            Rating = detail.Rating,
            Reviews = detail.Reviews,
        };
    }

    public static MonitorItemObservation FromError(
        MonitorTargetPlan plan,
        Exception exception,
        string runId,
        int cycle,
        DateTimeOffset observedAt)
    {
        var profile = MarketplaceRegistry.Resolve(plan.Target.Domain);
        return new MonitorItemObservation
        {
            ObservedAt = FormatTimestamp(observedAt),
            RunId = runId,
            Cycle = cycle,
            Domain = plan.Target.Domain,
            Asin = plan.Target.Asin,
            GroupId = plan.GroupId,
            Label = plan.Label,
            Url = profile.DetailUrl(plan.Target.Asin),
            Currency = profile.CurrencyCode,
            Error = exception.Message,
        };
    }

    public static MonitorGroupObservation FromGroup(
        MonitorGroupPlan group,
        IReadOnlyDictionary<string, MonitorItemObservation> observations,
        string runId,
        int cycle,
        DateTimeOffset observedAt)
    {
        var items = new List<MonitorGroupItemObservation>();
        var available = 0;
        decimal sum = 0;
        var complete = true;

        foreach (var target in group.Items)
        {
            observations.TryGetValue(target.Target.Key, out var item);
            var originalPrice = item?.PriceAmount;
            var originalCurrency = item?.Currency ?? string.Empty;
            var quantity = Math.Max(1, target.Quantity);
            var rate = ResolveRate(group, originalCurrency);
            decimal? price = originalPrice is null
                ? null
                : rate is null
                    ? null
                    : originalPrice.Value * rate.Value;
            decimal? lineTotal = price is null
                ? null
                : price.Value * quantity;
            decimal? originalLineTotal = originalPrice is null
                ? null
                : originalPrice.Value * quantity;

            if (price is not null)
            {
                available++;
                sum += lineTotal!.Value;
            }
            else
            {
                complete = false;
            }

            items.Add(new MonitorGroupItemObservation
            {
                Domain = target.Target.Domain,
                Asin = target.Target.Asin,
                Quantity = quantity,
                PriceAmount = price,
                OriginalCurrency = originalCurrency,
                OriginalPriceAmount = originalPrice,
                OriginalLineTotal = originalLineTotal,
                FxRate = rate,
                LineTotal = lineTotal,
                Error = item?.Error,
            });
        }

        var total = complete
            ? sum
            : group.AllowIncomplete && available > 0
                ? sum
                : (decimal?)null;

        return new MonitorGroupObservation
        {
            ObservedAt = FormatTimestamp(observedAt),
            RunId = runId,
            Cycle = cycle,
            GroupId = group.Id,
            Name = group.Name,
            Domains = group.Domains.ToList(),
            Currency = group.Currency,
            TotalMax = group.TotalMax,
            Total = total,
            Complete = complete,
            ItemsCount = group.Items.Count,
            AvailableCount = available,
            MissingCount = group.Items.Count - available,
            Items = items,
        };
    }

    private static decimal? ResolveRate(
        MonitorGroupPlan group,
        string sourceCurrency)
    {
        if (string.IsNullOrWhiteSpace(sourceCurrency)
            || string.Equals(
                sourceCurrency,
                group.Currency,
                StringComparison.OrdinalIgnoreCase))
        {
            return 1m;
        }

        if (group.FxRates.TryGetValue(sourceCurrency, out var rate)
            && rate > 0)
        {
            return rate;
        }

        // Legacy single-currency plans may not carry an FX table. Treat the
        // group currency as the source when no conversion is needed.
        return string.Equals(
            sourceCurrency,
            group.Currency,
            StringComparison.OrdinalIgnoreCase)
            ? 1m
            : null;
    }

    public static decimal? ParsePrice(IMarketplaceProfile profile, string value) =>
        profile.Format.TryParsePrice(value, out var amount) ? amount : null;

    public static string FormatAmount(decimal amount) =>
        amount.ToString("0.##", CultureInfo.InvariantCulture);

    public static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString(
            "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
            CultureInfo.InvariantCulture);
}
