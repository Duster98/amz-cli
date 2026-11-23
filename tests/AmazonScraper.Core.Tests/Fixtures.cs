using System.IO.Compression;
using System.Text.Json.Nodes;

namespace AmazonScraper.Core.Tests;

internal static class Fixtures
{
    public static string Load(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return reader.ReadToEnd();
    }

    public static JsonNode LoadJson(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        return JsonNode.Parse(File.ReadAllText(path))!;
    }

    public static string LoadText(string name)
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
    }
}
