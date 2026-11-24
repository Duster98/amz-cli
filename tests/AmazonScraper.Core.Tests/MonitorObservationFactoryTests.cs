using AmazonScraper.Core.Models;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Core.Tests;

public class MonitorObservationFactoryTests
{
    [Fact]
    public void FromDetail_keeps_standard_price_and_exposes_prime_price()
    {
        var plan = new MonitorTargetPlan
        {
            Target = new MonitorTarget("it", "B0H7MTVZ32"),
        };
        var detail = new ProductDetail
        {
            Asin = "B0H7MTVZ32",
            Price = "149,99 €",
            Prime = new PrimePricing
            {
                Price = "129,99 €",
                OriginalPrice = "149,99 €",
                Discount = "-13%",
            },
        };

        var observation = MonitorObservationFactory.FromDetail(
            detail,
            plan,
            "run",
            1,
            DateTimeOffset.Parse("2026-11-24T10:00:00Z"));

        Assert.Equal(149.99m, observation.PriceAmount);
        Assert.Equal("129,99 €", observation.PrimePrice);
        Assert.Equal(129.99m, observation.PrimePriceAmount);
        Assert.False(observation.PrimeOnly);
    }

    [Fact]
    public void FromDetail_marks_a_prime_only_offer()
    {
        var plan = new MonitorTargetPlan
        {
            Target = new MonitorTarget("it", "B0H71FFLXB"),
        };
        var detail = new ProductDetail
        {
            Asin = "B0H71FFLXB",
            Price = "109,99 €",
            Prime = new PrimePricing
            {
                Price = "109,99 €",
                Discount = "-17%",
                Only = true,
            },
        };

        var observation = MonitorObservationFactory.FromDetail(
            detail,
            plan,
            "run",
            1,
            DateTimeOffset.Parse("2026-11-24T10:00:00Z"));

        Assert.Equal(109.99m, observation.PriceAmount);
        Assert.Equal(109.99m, observation.PrimePriceAmount);
        Assert.True(observation.PrimeOnly);
    }
}
