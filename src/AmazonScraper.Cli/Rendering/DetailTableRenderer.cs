using AmazonScraper.Core.Formatting;
using AmazonScraper.Core.Models;
using Spectre.Console;

namespace AmazonScraper.Cli.Rendering;

public static class DetailTableRenderer
{
    private static readonly Style LabelStyle = new(Color.Yellow);
    private static readonly Style HeaderStyle = new(Color.Cyan, decoration: Decoration.Bold);
    private static readonly Style BorderStyle = new(Color.Cyan);

    public static void Render(
        IAnsiConsole console,
        ProductDetail detail,
        bool includeVariants = false,
        bool includeRating = false,
        bool includeReviews = false)
    {
        var table = new Table
        {
            Border = TableBorder.HeavyHead,
            BorderStyle = BorderStyle,
        };

        table.AddColumn(new TableColumn(new Text("Field", LabelStyle)) { Width = 16, NoWrap = true });
        table.AddColumn(new TableColumn(new Text("Value", HeaderStyle)) { Width = 80 });

        var rows = new List<(string Field, string Value)>
        {
            ("ASIN", detail.Asin),
            ("URL", detail.Url),
            ("Title", TextCleaner.Clean(detail.Title)),
            ("Brand", detail.Brand),
            ("Price", detail.Price),
            ("Original Price", detail.OriginalPrice),
            ("Discount", detail.Discount),
            ("Rating", detail.Rating),
            ("Reviews", detail.Reviews),
            ("Seller", detail.Seller),
            ("Shipped by", detail.ShippedBy),
            ("Returns", detail.Returns),
            ("Availability", detail.Availability),
        };

        if (!string.IsNullOrEmpty(detail.ModelName))
        {
            var brandIndex = rows.FindIndex(row =>
                string.Equals(row.Field, "Brand", StringComparison.Ordinal));
            rows.Insert(
                brandIndex >= 0 ? brandIndex + 1 : rows.Count,
                ("Model", TextCleaner.Clean(detail.ModelName)));
        }

        if (detail.Charger is { } charger)
        {
            var availabilityIndex = rows.FindIndex(row =>
                string.Equals(row.Field, "Availability", StringComparison.Ordinal));
            var insertAt = availabilityIndex >= 0
                ? availabilityIndex + 1
                : rows.Count;
            var chargerValue = charger.Included switch
            {
                false => "Not included",
                true => "Included",
                _ => "-",
            };
            if (!string.IsNullOrEmpty(charger.Label)
                && !string.Equals(
                    charger.Label,
                    chargerValue,
                    StringComparison.OrdinalIgnoreCase))
            {
                chargerValue += $" ({TextCleaner.Clean(charger.Label)})";
            }

            rows.Insert(insertAt++, ("Charger", chargerValue));
            if (!string.IsNullOrEmpty(charger.Note))
            {
                rows.Insert(insertAt++, ("Charger note", TextCleaner.Clean(charger.Note)));
            }
            if (!string.IsNullOrEmpty(charger.BoxContents))
            {
                rows.Insert(insertAt, ("Box contents", TextCleaner.Clean(charger.BoxContents)));
            }
        }

        if (includeRating
            && detail.RatingDetails is { } ratingDetails
            && ratingDetails.Histogram.Count > 0)
        {
            var reviewsIndex = rows.FindIndex(row =>
                string.Equals(row.Field, "Reviews", StringComparison.Ordinal));
            rows.Insert(
                reviewsIndex >= 0 ? reviewsIndex + 1 : rows.Count,
                ("Rating breakdown", FormatHistogram(ratingDetails.Histogram)));
        }

        if (includeReviews && detail.TopReviews is { } topReviews)
        {
            var ratingIndex = rows.FindIndex(row =>
                string.Equals(row.Field, "Rating breakdown", StringComparison.Ordinal));
            var reviewsIndex = rows.FindIndex(row =>
                string.Equals(row.Field, "Reviews", StringComparison.Ordinal));
            var insertAt = ratingIndex >= 0
                ? ratingIndex + 1
                : reviewsIndex >= 0
                    ? reviewsIndex + 1
                    : rows.Count;
            rows.Insert(insertAt, ("Reviews found", FormatReviewSummary(topReviews)));
        }

        if (detail.AmazonChoice == true)
        {
            var choice = string.IsNullOrEmpty(detail.AmazonChoiceText)
                ? "Amazon's Choice"
                : detail.AmazonChoiceText;
            if (!string.IsNullOrEmpty(detail.AmazonChoiceFor))
            {
                choice += $" · {detail.AmazonChoiceFor}";
            }

            var discountIndex = rows.FindIndex(row =>
                string.Equals(row.Field, "Discount", StringComparison.Ordinal));
            rows.Insert(
                discountIndex >= 0 ? discountIndex + 1 : rows.Count,
                ("Choice", TextCleaner.Clean(choice)));
        }

        if (detail.BestSellerRank is not null
            || !string.IsNullOrEmpty(detail.BestSellerLabel)
            || !string.IsNullOrEmpty(detail.BestSellerCategory)
            || !string.IsNullOrEmpty(detail.BestSellerUrl))
        {
            var bestSeller = detail.BestSellerLabel ?? string.Empty;
            if (detail.BestSellerRank is { } rank
                && !bestSeller.Contains($"#{rank}", StringComparison.Ordinal))
            {
                bestSeller = string.IsNullOrEmpty(bestSeller)
                    ? $"#{rank}"
                    : $"{bestSeller} (#{rank})";
            }
            if (!string.IsNullOrEmpty(detail.BestSellerCategory)
                && !bestSeller.Contains(
                    detail.BestSellerCategory,
                    StringComparison.OrdinalIgnoreCase))
            {
                bestSeller = string.IsNullOrEmpty(bestSeller)
                    ? detail.BestSellerRank is { } categoryRank
                        ? $"#{categoryRank} in {detail.BestSellerCategory}"
                        : detail.BestSellerCategory
                    : $"{bestSeller} · {detail.BestSellerCategory}";
            }

            var anchor = rows.FindIndex(row =>
                row.Field is "Choice" or "Discount" or "Price");
            var insertAt = anchor >= 0 ? anchor + 1 : rows.Count;
            rows.Insert(insertAt++, ("Best seller", TextCleaner.Clean(bestSeller)));
            if (!string.IsNullOrEmpty(detail.BestSellerUrl))
            {
                rows.Insert(insertAt, ("Best seller URL", detail.BestSellerUrl));
            }
        }

        if (!string.IsNullOrEmpty(detail.Deal))
        {
            rows.Insert(7, ("Deal", detail.Deal));
            var dealRow = 8;
            if (!string.IsNullOrEmpty(detail.DealId))
            {
                rows.Insert(dealRow++, ("Deal ID", detail.DealId));
            }
            if (!string.IsNullOrEmpty(detail.DealEndsText))
            {
                rows.Insert(dealRow++, ("Deal ends", detail.DealEndsText));
            }
            if (detail.DealEndsInDays is { } days)
            {
                rows.Insert(dealRow++, ("Deal in", FormatDealDays(days)));
            }
            if (detail.DealHasCountdown is true)
            {
                rows.Insert(dealRow, ("Countdown", "yes"));
            }
        }

        if (detail.Delivery is { } delivery)
        {
            rows.Add(("Delivery cost", delivery.Cost));
            rows.Add(("Delivery time", delivery.Time));
            rows.Add(("Delivery location", delivery.Location));
            rows.Add(("Delivery cutoff", delivery.Cutoff));

            if (delivery.Options is { Count: > 1 } options)
            {
                var optionIndex = 2;
                foreach (var option in options.Skip(1))
                {
                    var parts = new[]
                    {
                        option.Type,
                        option.Cost,
                        option.Time,
                        string.IsNullOrEmpty(option.Cutoff)
                            ? string.Empty
                            : $"cutoff: {option.Cutoff}",
                    }.Where(part => !string.IsNullOrEmpty(part));
                    rows.Add((
                        $"Delivery opt {optionIndex++}",
                        string.Join(" · ", parts)));
                }
            }
        }

        if (detail.ImportFees is { } importFees)
        {
            if (!string.IsNullOrEmpty(importFees.Price)
                && !string.Equals(importFees.PriceBasis, "current", StringComparison.OrdinalIgnoreCase))
            {
                var basis = string.Equals(
                        importFees.PriceBasis,
                        "list",
                        StringComparison.OrdinalIgnoreCase)
                    ? " (list price)"
                    : string.Equals(
                        importFees.PriceBasis,
                        "other",
                        StringComparison.OrdinalIgnoreCase)
                        ? " (non-current)"
                        : string.Empty;
                rows.Add(("Import price", $"{importFees.Price}{basis}"));
            }
            if (!string.IsNullOrEmpty(importFees.Text))
            {
                rows.Add(("Import fees", importFees.Text));
            }
            if (!string.IsNullOrEmpty(importFees.ImportCharges))
            {
                rows.Add(("Import charges", importFees.ImportCharges));
            }
            if (!string.IsNullOrEmpty(importFees.LandedTotal))
            {
                rows.Add(("Landed total", importFees.LandedTotal));
            }
        }

        rows.Add(("Coupon", detail.Coupon));
        rows.Add(("Image", detail.Image));

        if (!string.IsNullOrEmpty(detail.FrequentlyReturned))
        {
            var availabilityIndex = rows.FindIndex(row =>
                string.Equals(row.Field, "Availability", StringComparison.Ordinal));
            rows.Insert(
                availabilityIndex >= 0 ? availabilityIndex + 1 : rows.Count,
                ("Alert", detail.FrequentlyReturned));
        }

        if (detail.Variants is { Items.Count: > 0 } variantSummary)
        {
            rows.Add((
                "Variants",
                $"{variantSummary.Items.Count} (use --variants for the list)"));
        }

        foreach (var (field, value) in rows)
        {
            table.AddRow(
                new Text(field, LabelStyle),
                new Text(string.IsNullOrEmpty(value) ? "-" : value));
        }

        if (detail.BuyingOptions is { } buyingOptions
            && (buyingOptions.Count > 0 || buyingOptions.Options.Count > 0))
        {
            table.AddEmptyRow();

            var summaryParts = new List<string>();
            if (buyingOptions.Count > 0)
            {
                summaryParts.Add($"{buyingOptions.Count} options");
            }
            if (!string.IsNullOrEmpty(buyingOptions.LowestPrice))
            {
                summaryParts.Add($"from {buyingOptions.LowestPrice}");
            }

            table.AddRow(
                new Text("Buying options", HeaderStyle),
                new Text(string.Join(" · ", summaryParts)));

            var index = 1;
            foreach (var option in buyingOptions.Options)
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(option.Condition))
                {
                    parts.Add(option.Condition);
                }
                if (!string.IsNullOrEmpty(option.Price))
                {
                    parts.Add(option.Price);
                }
                if (!string.IsNullOrEmpty(option.Seller))
                {
                    parts.Add(string.IsNullOrEmpty(option.SellerId)
                        ? option.Seller
                        : $"{option.Seller} ({option.SellerId})");
                }
                if (!string.IsNullOrEmpty(option.Shipping))
                {
                    parts.Add($"shipping {option.Shipping}");
                }
                if (!string.IsNullOrEmpty(option.Delivery))
                {
                    parts.Add(option.Delivery);
                }

                table.AddRow(
                    new Text($"  Option {index}", LabelStyle),
                    new Text(string.Join(" · ", parts)));
                table.AddRow(
                    new Text("    Offer ID", LabelStyle),
                    new Text(string.IsNullOrEmpty(option.OfferListingId)
                        ? "-"
                        : option.OfferListingId));
                table.AddRow(
                    new Text("    Detail", LabelStyle),
                    new Text(string.IsNullOrEmpty(option.DetailUrl)
                        ? "-"
                        : option.DetailUrl));
                index++;
            }
        }

        if (detail.Images.Count > 0)
        {
            table.AddEmptyRow();
            table.AddRow(new Text("Images", HeaderStyle), new Text(string.Empty));
            foreach (var image in detail.Images)
            {
                table.AddRow(new Text(string.Empty), new Text(image));
            }
        }

        if (detail.SafetyWarnings.Count > 0)
        {
            table.AddEmptyRow();
            table.AddRow(new Text("Safety warnings", HeaderStyle), new Text(string.Empty));
            foreach (var warning in detail.SafetyWarnings)
            {
                table.AddRow(
                    new Text(string.Empty),
                    new Text($"⚠ {TextCleaner.Clean(warning)}"));
            }
        }

        if (detail.Description.Count > 0)
        {
            table.AddEmptyRow();
            table.AddRow(new Text("Description", HeaderStyle), new Text(string.Empty));
            foreach (var bullet in detail.Description)
            {
                table.AddRow(new Text(string.Empty), new Text($"• {TextCleaner.Clean(bullet)}"));
            }
        }

        if (detail.Specs.Count > 0)
        {
            table.AddEmptyRow();
            table.AddRow(new Text("Specs", HeaderStyle), new Text(string.Empty));
            foreach (var (key, value) in detail.Specs)
            {
                table.AddRow(new Text($"  {key}"), new Text(value));
            }
        }

        console.Write(table);

        if (includeVariants && detail.Variants is { Items.Count: > 0 } variants)
        {
            console.WriteLine();
            VariantTableRenderer.Render(console, variants);
        }

        if (includeReviews && detail.TopReviews is { Items.Count: > 0 } reviews)
        {
            console.WriteLine();
            ReviewTableRenderer.Render(console, reviews);
        }
    }

    private static string FormatHistogram(Dictionary<string, int> histogram) =>
        string.Join(
            " · ",
            histogram.Select(pair => $"{pair.Key}★ {pair.Value}%"));

    private static string FormatReviewSummary(TopReviews reviews)
    {
        var parts = new List<string> { reviews.Count.ToString() };
        if (reviews.LocalCount > 0)
        {
            parts.Add($"{reviews.LocalCount} local");
        }
        if (reviews.InternationalCount > 0)
        {
            parts.Add($"{reviews.InternationalCount} international");
        }

        return string.Join(" · ", parts);
    }

    private static string FormatDealDays(int days) =>
        days switch
        {
            0 => "today",
            1 => "in 1 day",
            _ => $"in {days} days",
        };
}
