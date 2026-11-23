using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CurlImpersonate.Enums;

namespace AmazonScraper.Cli.Storage;

/// <summary>
/// Persistent HTTP settings shared by the CLI, the Web UI and the monitors.
/// The values only describe defaults: command-line flags always win over the
/// saved file, and marketplace defaults win over an "auto" value.
/// </summary>
public sealed class HttpSettings
{
    public const string Auto = "auto";
    public const double DefaultTimeoutSeconds = 30;
    public const double MinTimeoutSeconds = 5;
    public const double MaxTimeoutSeconds = 120;
    public const int CurrentSchema = 1;

    private static readonly Dictionary<string, string> BrowserNames =
        Enum.GetNames<BrowserProfile>()
            .ToDictionary(
                name => name,
                name => name,
                StringComparer.OrdinalIgnoreCase);

    public int Schema { get; set; } = CurrentSchema;

    public string Transport { get; set; } = Auto;

    public string Browser { get; set; } = Auto;

    public string UserAgent { get; set; } = string.Empty;

    public bool BrowserHeaders { get; set; }

    public double TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    public bool FallbackToCurl { get; set; } = true;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public static HttpSettings Default => new();

    public static IReadOnlyList<string> AvailableBrowsers =>
        BrowserNames.Values
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static bool TryNormalizeTransport(
        string? value,
        out string transport)
    {
        transport = (value ?? string.Empty).Trim().ToLowerInvariant();
        return transport is Auto or "curl" or "http";
    }

    public static bool TryNormalizeBrowser(
        string? value,
        out string browser)
    {
        var candidate = (value ?? string.Empty).Trim();
        if (candidate.Length == 0 || candidate.Equals(Auto, StringComparison.OrdinalIgnoreCase))
        {
            browser = Auto;
            return true;
        }

        if (BrowserNames.TryGetValue(candidate, out var canonical))
        {
            browser = canonical;
            return true;
        }

        browser = Auto;
        return false;
    }

    public static string NormalizeUserAgent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            if (!char.IsControl(character))
            {
                builder.Append(character);
            }
        }

        var normalized = builder.ToString().Trim();
        return normalized.Length <= 256 ? normalized : normalized[..256];
    }

    public HttpSettings Normalize()
    {
        var transport = TryNormalizeTransport(Transport, out var normalizedTransport)
            ? normalizedTransport
            : Auto;
        _ = TryNormalizeBrowser(Browser, out var browser);
        var timeout = TimeoutSeconds;
        if (!double.IsFinite(timeout) || timeout <= 0)
        {
            timeout = DefaultTimeoutSeconds;
        }
        else
        {
            timeout = Math.Clamp(
                Math.Round(timeout, 3),
                MinTimeoutSeconds,
                MaxTimeoutSeconds);
        }

        return new HttpSettings
        {
            Schema = CurrentSchema,
            Transport = transport,
            Browser = browser,
            UserAgent = NormalizeUserAgent(UserAgent),
            BrowserHeaders = BrowserHeaders,
            TimeoutSeconds = timeout,
            FallbackToCurl = FallbackToCurl,
            UpdatedAt = UpdatedAt == default ? DateTimeOffset.UtcNow : UpdatedAt,
        };
    }
}

public sealed class HttpSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly object _gate = new();
    private readonly string _path;

    public HttpSettingsStore(string? path = null)
    {
        _path = System.IO.Path.GetFullPath(
            path ?? UserStatePaths.HttpSettingsPath());
    }

    public string FilePath => _path;

    public static HttpSettingsStore Default { get; } = new();

    public HttpSettings Load()
    {
        lock (_gate)
        {
            return LoadUnlocked();
        }
    }

    public HttpSettings Save(HttpSettings? settings)
    {
        lock (_gate)
        {
            var normalized = (settings ?? new HttpSettings()).Normalize();
            normalized.UpdatedAt = DateTimeOffset.UtcNow;
            WriteUnlocked(normalized);
            return normalized;
        }
    }

    public HttpSettings Reset() => Save(new HttpSettings());

    private HttpSettings LoadUnlocked()
    {
        if (!File.Exists(_path))
        {
            return new HttpSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<HttpSettings>(
                File.ReadAllText(_path),
                Options);
            return (settings ?? new HttpSettings()).Normalize();
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or JsonException
                or NotSupportedException)
        {
            // A damaged file must not prevent scraping.
            return new HttpSettings();
        }
    }

    private void WriteUnlocked(HttpSettings settings)
    {
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            TrySetUnixMode(
                directory,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute);
        }

        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(settings, Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        TrySetUnixMode(
            temporary,
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temporary, _path, overwrite: true);
        TrySetUnixMode(
            _path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static void TrySetUnixMode(string path, UnixFileMode mode)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, mode);
        }
        catch (Exception)
        {
            // Best effort: a read-only state directory must not break scraping.
        }
    }
}
