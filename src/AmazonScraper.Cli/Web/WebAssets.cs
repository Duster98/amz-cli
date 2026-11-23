using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace AmazonScraper.Cli.Web;

public sealed record WebAsset(
    byte[] Content,
    string ContentType,
    string ETag,
    bool Immutable);

public sealed class WebAssetStore
{
    private const string EmbeddedResourceName =
        "AmazonScraper.Cli.WebAssets.web.zip";

    private readonly Dictionary<string, WebAsset> _assets =
        new(StringComparer.Ordinal);

    private WebAssetStore()
    {
    }

    public bool IsAvailable => _assets.Count > 0;

    public bool IsFallback { get; private set; }

    public string BuildHash { get; private set; } = "missing";

    public static WebAssetStore Load(string? webDirectory)
    {
        var store = new WebAssetStore();
        try
        {
            if (!string.IsNullOrWhiteSpace(webDirectory))
            {
                store.LoadDirectory(Path.GetFullPath(webDirectory));
            }
            else
            {
                store.LoadEmbedded();
            }
        }
        catch (Exception)
        {
            // A corrupt or unreadable bundle must not prevent the CLI from
            // starting: serve the build-instructions fallback instead.
            store._assets.Clear();
        }

        if (!store._assets.ContainsKey("index.html"))
        {
            store.AddFallback();
        }

        if (store._assets.TryGetValue("index.html", out var index))
        {
            store.BuildHash = Convert.ToHexString(
                    SHA256.HashData(index.Content))
                .ToLowerInvariant()[..12];
        }

        return store;
    }

    public bool TryGet(string? path, out WebAsset asset)
    {
        var normalized = Normalize(path);
        if (normalized.Length == 0)
        {
            normalized = "index.html";
        }

        return _assets.TryGetValue(normalized, out asset!);
    }

    public bool TryGetIndex(out WebAsset asset) =>
        _assets.TryGetValue("index.html", out asset!);

    private void LoadEmbedded()
    {
        var assembly = typeof(WebAssetStore).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName);
        if (stream is null)
        {
            return;
        }

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/')
                || entry.Length == 0)
            {
                continue;
            }

            using var source = entry.Open();
            using var memory = new MemoryStream();
            source.CopyTo(memory);
            Add(Normalize(entry.FullName), memory.ToArray());
        }
    }

    private void LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        var root = Path.TrimEndingDirectorySeparator(directory);
        foreach (var file in Directory.EnumerateFiles(
                     root,
                     "*",
                     SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file)
                .Replace(Path.DirectorySeparatorChar, '/');
            if (relative.StartsWith('.'))
            {
                continue;
            }

            Add(Normalize(relative), File.ReadAllBytes(file));
        }
    }

    private void Add(string path, byte[] content)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains("..", StringComparison.Ordinal))
        {
            return;
        }

        _assets[path] = new WebAsset(
            content,
            ContentType(path),
            "\"" + Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant() + "\"",
            Immutable: !string.Equals(path, "index.html", StringComparison.Ordinal));
    }

    private void AddFallback()
    {
        IsFallback = true;
        const string html = """
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>AMZ CLI · Web UI not built</title>
              <style>
                :root { color-scheme: dark; }
                body { margin: 0; min-height: 100vh; display: grid; place-items: center;
                  background: #0f1115; color: #e8e6e1;
                  font: 16px/1.6 ui-sans-serif, system-ui, sans-serif; }
                main { max-width: 640px; padding: 32px; }
                h1 { font-size: 24px; margin: 0 0 12px; }
                code { display: block; margin-top: 16px; padding: 14px 16px;
                  border: 1px solid #2a2f3a; border-radius: 10px;
                  background: #171a21; color: #ffc46b; }
                p { color: #a8adb8; }
              </style>
            </head>
            <body>
              <main>
                <h1>Web interface not built</h1>
                <p>This CLI build does not contain the web app assets.</p>
                <code>cd dotnet/web &amp;&amp; ./build.sh</code>
                <p>Then restart <strong>amz-cli web</strong>.</p>
              </main>
            </body>
            </html>
            """;
        Add("index.html", Encoding.UTF8.GetBytes(html));
    }

    private static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path.Trim().Replace('\\', '/').TrimStart('/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }
        if (normalized.StartsWith("dist/", StringComparison.Ordinal))
        {
            normalized = normalized["dist/".Length..];
        }
        return normalized;
    }

    private static string ContentType(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" or ".mjs" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".txt" => "text/plain; charset=utf-8",
            ".map" => "application/json; charset=utf-8",
            _ => "application/octet-stream",
        };
    }
}
