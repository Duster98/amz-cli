using System.Globalization;

namespace AmazonScraper.Core.Monitoring;

public static class MonitorCsv
{
    public const string ItemHeader =
        "schema_version,record_type,observed_at,run_id,cycle,domain,asin,group_id,label,url,title," +
        "price,price_amount,currency,original_price,original_price_amount,discount,coupon,deal," +
        "availability,seller,shipped_by,rating,reviews,triggered_rules,error," +
        "deal_id,deal_ends_text,deal_ends_on,deal_ends_in_days,deal_has_countdown";

    public const string GroupHeader =
        "schema_version,record_type,observed_at,run_id,cycle,group_id,name,domains,currency," +
        "total_max,total,complete,items_count,available_count,missing_count,triggered_rules";

    public const string AlertHeader =
        "schema_version,record_type,alerted_at,run_id,cycle,rule_id,type,domain,asin,group_id," +
        "currency,threshold,observed,complete,message,url";

    public static string FormatItem(MonitorItemObservation observation)
    {
        var fields = new[]
        {
            observation.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            observation.RecordType,
            observation.ObservedAt,
            observation.RunId,
            observation.Cycle.ToString(CultureInfo.InvariantCulture),
            observation.Domain,
            observation.Asin,
            observation.GroupId,
            observation.Label,
            observation.Url,
            observation.Title,
            observation.Price,
            FormatDecimal(observation.PriceAmount),
            observation.Currency,
            observation.OriginalPrice,
            FormatDecimal(observation.OriginalPriceAmount),
            observation.Discount,
            observation.Coupon,
            observation.Deal,
            observation.Availability,
            observation.Seller,
            observation.ShippedBy,
            observation.Rating,
            observation.Reviews,
            string.Join('|', observation.TriggeredRules),
            observation.Error,
            observation.DealId,
            observation.DealEndsText,
            observation.DealEndsOn,
            FormatNullable(observation.DealEndsInDays),
            FormatNullable(observation.DealHasCountdown),
        };

        return Join(fields);
    }

    public static string FormatGroup(MonitorGroupObservation observation)
    {
        var fields = new[]
        {
            observation.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            observation.RecordType,
            observation.ObservedAt,
            observation.RunId,
            observation.Cycle.ToString(CultureInfo.InvariantCulture),
            observation.GroupId,
            observation.Name,
            string.Join('|', observation.Domains),
            observation.Currency,
            FormatDecimal(observation.TotalMax),
            FormatDecimal(observation.Total),
            observation.Complete ? "true" : "false",
            observation.ItemsCount.ToString(CultureInfo.InvariantCulture),
            observation.AvailableCount.ToString(CultureInfo.InvariantCulture),
            observation.MissingCount.ToString(CultureInfo.InvariantCulture),
            string.Join('|', observation.TriggeredRules),
        };

        return Join(fields);
    }

    public static string FormatAlert(MonitorAlert alert)
    {
        var fields = new[]
        {
            alert.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            alert.RecordType,
            alert.AlertedAt,
            alert.RunId,
            alert.Cycle.ToString(CultureInfo.InvariantCulture),
            alert.RuleId,
            alert.Type,
            alert.Domain,
            alert.Asin,
            alert.GroupId,
            alert.Currency,
            FormatDecimal(alert.Threshold),
            FormatDecimal(alert.Observed),
            alert.Complete is null ? string.Empty : alert.Complete.Value ? "true" : "false",
            alert.Message,
            alert.Url,
        };

        return Join(fields);
    }

    private static string Join(IEnumerable<string?> fields) =>
        string.Join(',', fields.Select(Escape));

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var mustQuote = value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            || char.IsWhiteSpace(value[0])
            || char.IsWhiteSpace(value[^1]);
        if (!mustQuote)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string FormatDecimal(decimal? value) =>
        value is null
            ? string.Empty
            : value.Value.ToString("0.##########", CultureInfo.InvariantCulture);

    private static string FormatNullable(int? value) =>
        value is null
            ? string.Empty
            : value.Value.ToString(CultureInfo.InvariantCulture);

    private static string FormatNullable(bool? value) =>
        value is null
            ? string.Empty
            : value.Value ? "true" : "false";
}
