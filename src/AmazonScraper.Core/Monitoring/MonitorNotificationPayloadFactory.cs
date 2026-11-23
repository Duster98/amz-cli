namespace AmazonScraper.Core.Monitoring;

public static class MonitorNotificationPayloadFactory
{
    public static MonitorNotificationPayload ItemAlert(
        MonitorAlert alert,
        MonitorItemObservation item,
        DateTimeOffset now)
    {
        var title = alert.Type switch
        {
            "item_max_price" => "Price below threshold",
            "item_rise_percent" => "Price increase detected",
            _ => "Price drop detected",
        };
        return new MonitorNotificationPayload
        {
            Kind = "alert",
            Title = title,
            Body = alert.Message,
            CreatedAt = MonitorObservationFactory.FormatTimestamp(now),
            Currency = alert.Currency,
            Items =
            [
                new MonitorNotificationItem
                {
                    Domain = alert.Domain,
                    Asin = alert.Asin,
                    Title = item.Title,
                    Url = item.Url,
                    Type = alert.Type,
                    Message = alert.Message,
                    Threshold = alert.Threshold,
                    Observed = alert.Observed,
                    Quantity = 1,
                    LineTotal = alert.Observed,
                    Currency = alert.Currency,
                    Price = item.Price,
                    OriginalPrice = item.OriginalPrice,
                    Discount = item.Discount,
                    Coupon = item.Coupon,
                    Deal = item.Deal,
                    DealId = item.DealId,
                    DealEndsText = item.DealEndsText,
                    DealEndsOn = item.DealEndsOn,
                },
            ],
        };
    }

    public static MonitorNotificationPayload GroupAlert(
        MonitorAlert alert,
        MonitorGroupObservation group,
        IReadOnlyDictionary<string, MonitorItemObservation> observations,
        DateTimeOffset now)
    {
        var items = new List<MonitorNotificationItem>();
        foreach (var groupItem in group.Items)
        {
            observations.TryGetValue(
                $"{groupItem.Domain}:{groupItem.Asin}",
                out var observation);
            items.Add(new MonitorNotificationItem
            {
                Domain = groupItem.Domain,
                Asin = groupItem.Asin,
                Title = observation?.Title,
                Url = observation?.Url,
                Type = "group_item",
                Message = groupItem.Error,
                Quantity = groupItem.Quantity,
                LineTotal = groupItem.LineTotal,
                Currency = group.Currency,
                Price = observation?.Price,
                OriginalPrice = observation?.OriginalPrice,
                Discount = observation?.Discount,
                Coupon = observation?.Coupon,
                Deal = observation?.Deal,
                DealId = observation?.DealId,
                DealEndsText = observation?.DealEndsText,
                DealEndsOn = observation?.DealEndsOn,
                Error = groupItem.Error,
            });
        }

        return new MonitorNotificationPayload
        {
            Kind = "group_alert",
            Title = "Group total below threshold",
            Body = alert.Message,
            CreatedAt = MonitorObservationFactory.FormatTimestamp(now),
            GroupId = group.GroupId,
            GroupName = group.Name,
            GroupTotal = group.Total,
            GroupThreshold = group.TotalMax,
            Currency = group.Currency,
            Items = items,
        };
    }

    public static MonitorNotificationPayload ErrorDigest(
        IReadOnlyList<MonitorErrorEvent> errors,
        DateTimeOffset now) =>
        new()
        {
            Kind = "errors",
            Title = "Monitoring errors",
            Body = errors.Count == 1
                ? "1 error in the cycle"
                : $"{errors.Count} errors in the cycle",
            CreatedAt = MonitorObservationFactory.FormatTimestamp(now),
            Items = errors.Select(error => new MonitorNotificationItem
            {
                Domain = error.Domain,
                Asin = error.Asin,
                Type = "error",
                Message = error.Message,
                Error = error.Message,
            }).ToList(),
        };

    public static MonitorNotificationPayload Event(
        string kind,
        string title,
        string body,
        DateTimeOffset now) =>
        new()
        {
            Kind = kind,
            Title = title,
            Body = body,
            CreatedAt = MonitorObservationFactory.FormatTimestamp(now),
        };

    public static MonitorNotificationPayload Test(DateTimeOffset now) =>
        Event(
            "test",
            "Test notification",
            "Desktop notifications are working.",
            now);

    public static MonitorNotificationPayload Update(
        string title,
        string body,
        string status,
        string? fromVersion,
        string? toVersion,
        bool? monitorsRestarted,
        string? backupPath,
        string? logPath,
        string? error,
        string trigger,
        DateTimeOffset now,
        string? notes = null) =>
        new()
        {
            Kind = "update",
            Title = title,
            Body = body,
            Notes = notes,
            CreatedAt = MonitorObservationFactory.FormatTimestamp(now),
            UpdateStatus = status,
            FromVersion = fromVersion,
            ToVersion = toVersion,
            MonitorsRestarted = monitorsRestarted,
            BackupPath = backupPath,
            LogPath = logPath,
            Error = error,
            Trigger = trigger,
        };
}
