using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Monitoring;

/// <summary>
/// Fingerprint of the runtime settings that a process actually started with.
/// It lets the Web UI detect that a saved binding has newer settings than the
/// running process without persisting Web-only state.
/// </summary>
public static class MonitorRuntimeSignature
{
    public static string Compute(
        int timerSeconds,
        int requestDelaySeconds,
        int jitterSeconds,
        MonitorFormat format,
        MonitorLayout layout,
        bool notify,
        IEnumerable<string> notifyOn,
        int? notifyCooldownSeconds,
        bool noAutoUpdate,
        string savePath,
        string saveFile)
    {
        var events = notify
            ? string.Join(
                ',',
                notifyOn
                    .Select(value => (value ?? string.Empty).Trim().ToLowerInvariant())
                    .Where(value => value.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal))
            : string.Empty;
        var text = string.Join(
            '|',
            timerSeconds.ToString(CultureInfo.InvariantCulture),
            requestDelaySeconds.ToString(CultureInfo.InvariantCulture),
            jitterSeconds.ToString(CultureInfo.InvariantCulture),
            format,
            layout,
            notify ? "1" : "0",
            events,
            notifyCooldownSeconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            noAutoUpdate ? "1" : "0",
            savePath,
            saveFile);
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(text)))
            .ToLowerInvariant();
    }
}
