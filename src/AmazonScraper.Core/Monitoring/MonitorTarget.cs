namespace AmazonScraper.Core.Monitoring;

/// <summary>
/// One product to monitor. The domain is the normalized marketplace id
/// (it, de, fr, uk, com, jp, es), never the host name.
/// </summary>
public sealed record MonitorTarget(string Domain, string Asin)
{
    public string Key => $"{Domain}:{Asin}";

    public override string ToString() => Key;
}
