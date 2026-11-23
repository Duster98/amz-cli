using System.Text.Encodings.Web;
using System.Text.Json;

namespace AmazonScraper.Core.Monitoring;

public static class MonitorJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Item(MonitorItemObservation observation) =>
        JsonSerializer.Serialize(observation, Options);

    public static string Group(MonitorGroupObservation observation) =>
        JsonSerializer.Serialize(observation, Options);

    public static string Alert(MonitorAlert alert) =>
        JsonSerializer.Serialize(alert, Options);
}
