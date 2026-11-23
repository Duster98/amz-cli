namespace AmazonScraper.Core.Monitoring;

public enum MonitorFormat
{
    JsonLines,
    Csv,
}

public enum MonitorLayout
{
    Auto,
    Combined,
    PerGroup,
    Single,
}

public enum PriceBaseline
{
    Original,
    FirstSeen,
    MaxSeen,
    LastSeen,
    Fixed,
}

public enum MonitorUnitKind
{
    Item,
    Group,
}

public sealed class MonitorException(string message) : Exception(message);
