namespace AmazonScraper.Core.Monitoring;

[Flags]
public enum MonitorNotificationEvents
{
    None = 0,
    Alerts = 1,
    Errors = 2,
    Blocks = 4,
    Fatal = 8,
    All = Alerts | Errors | Blocks | Fatal,
}

public enum MonitorNotificationSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record MonitorNotificationMessage(
    string Title,
    string Body,
    MonitorNotificationSeverity Severity = MonitorNotificationSeverity.Info,
    MonitorNotificationPayload? Payload = null);

public sealed record MonitorErrorEvent(
    string Domain,
    string? Asin,
    string Kind,
    string Message);

public static class MonitorNotificationEventsParser
{
    public static MonitorNotificationEvents Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return MonitorNotificationEvents.Alerts;
        }

        var events = MonitorNotificationEvents.None;
        foreach (var token in value.Split(
                     [',', ';', ' '],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            events |= token.ToLowerInvariant() switch
            {
                "alerts" or "alert" => MonitorNotificationEvents.Alerts,
                "errors" or "error" => MonitorNotificationEvents.Errors,
                "blocks" or "block" => MonitorNotificationEvents.Blocks,
                "fatal" => MonitorNotificationEvents.Fatal,
                "all" => MonitorNotificationEvents.All,
                _ => throw new MonitorException(
                    $"Unsupported notification event: '{token}'. " +
                    "Values: alerts, errors, blocks, fatal, all."),
            };
        }

        return events == MonitorNotificationEvents.None
            ? MonitorNotificationEvents.Alerts
            : events;
    }

    public static string Format(MonitorNotificationEvents events)
    {
        if (events == MonitorNotificationEvents.All)
        {
            return "all";
        }

        var parts = new List<string>();
        if (events.HasFlag(MonitorNotificationEvents.Alerts)) parts.Add("alerts");
        if (events.HasFlag(MonitorNotificationEvents.Errors)) parts.Add("errors");
        if (events.HasFlag(MonitorNotificationEvents.Blocks)) parts.Add("blocks");
        if (events.HasFlag(MonitorNotificationEvents.Fatal)) parts.Add("fatal");
        return parts.Count == 0 ? "none" : string.Join(',', parts);
    }
}
