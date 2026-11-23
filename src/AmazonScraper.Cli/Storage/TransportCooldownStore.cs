using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AmazonScraper.Core.Marketplaces;

namespace AmazonScraper.Cli.Storage;

public sealed record TransportCooldown(
    string Domain,
    DateTimeOffset Until,
    string Reason,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Remembers that HTTP was blocked for a marketplace and keeps subsequent
/// automatic requests on curl-impersonate for a bounded period. One small JSON
/// file per marketplace avoids read/modify/write races between the web server,
/// the monitor and CLI commands running at the same time.
/// </summary>
public sealed class TransportCooldownStore
{
    public static readonly TimeSpan DefaultCooldown = TimeSpan.FromHours(2);

    private const int CurrentSchema = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _directory;

    public TransportCooldownStore(string? directory = null)
    {
        _directory = System.IO.Path.GetFullPath(
            directory ?? System.IO.Path.Combine(
                UserStatePaths.Root(),
                "transport"));
    }

    public static TransportCooldownStore Default { get; } = new();

    public bool IsCurlPreferred(
        string domain,
        DateTimeOffset? now = null) =>
        GetActive(domain, now) is not null;

    public TransportCooldown? GetActive(
        string domain,
        DateTimeOffset? now = null)
    {
        var id = NormalizeDomain(domain);
        if (id is null)
        {
            return null;
        }

        var file = ReadFile(FilePath(id));
        if (file is null
            || file.Schema != CurrentSchema
            || !string.Equals(file.Transport, "curl", StringComparison.Ordinal)
            || file.Until == default)
        {
            return null;
        }

        var utcNow = now ?? DateTimeOffset.UtcNow;
        return file.Until > utcNow
            ? new TransportCooldown(
                id,
                file.Until,
                file.Reason ?? string.Empty,
                file.UpdatedAt)
            : null;
    }

    public bool RememberHttpFailure(
        string domain,
        string reason,
        TimeSpan? cooldown = null,
        DateTimeOffset? now = null)
    {
        var id = NormalizeDomain(domain);
        if (id is null)
        {
            return false;
        }

        try
        {
            var utcNow = now ?? DateTimeOffset.UtcNow;
            var duration = cooldown ?? DefaultCooldown;
            if (duration <= TimeSpan.Zero)
            {
                duration = DefaultCooldown;
            }

            var file = new CooldownFile
            {
                Schema = CurrentSchema,
                Domain = id,
                Transport = "curl",
                Until = utcNow + duration,
                Reason = Truncate(reason, 300),
                UpdatedAt = utcNow,
            };

            Directory.CreateDirectory(_directory);
            TrySetUnixMode(
                _directory,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute);

            var path = FilePath(id);
            var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(file, Options),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            TrySetUnixMode(
                temporary,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, path, overwrite: true);
            TrySetUnixMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return true;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or JsonException)
        {
            // A read-only state directory must not break scraping.
            return false;
        }
    }

    public IReadOnlyList<TransportCooldown> ListActive(
        DateTimeOffset? now = null)
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        var utcNow = now ?? DateTimeOffset.UtcNow;
        var results = new List<TransportCooldown>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(
                         _directory,
                         "*.json",
                         SearchOption.TopDirectoryOnly))
            {
                var file = ReadFile(path);
                if (file is null
                    || file.Schema != CurrentSchema
                    || !string.Equals(
                        file.Transport,
                        "curl",
                        StringComparison.Ordinal)
                    || file.Until <= utcNow)
                {
                    continue;
                }

                var id = NormalizeDomain(file.Domain);
                if (id is null)
                {
                    continue;
                }

                results.Add(new TransportCooldown(
                    id,
                    file.Until,
                    file.Reason ?? string.Empty,
                    file.UpdatedAt));
            }
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            return results;
        }

        return results
            .OrderBy(item => item.Domain, StringComparer.Ordinal)
            .ToArray();
    }

    public void Clear(string domain)
    {
        var id = NormalizeDomain(domain);
        if (id is null)
        {
            return;
        }

        try
        {
            File.Delete(FilePath(id));
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    private string FilePath(string domain) =>
        System.IO.Path.Combine(_directory, $"{domain}.json");

    private static string? NormalizeDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return null;
        }

        if (MarketplaceRegistry.TryResolve(domain, out var marketplace))
        {
            return marketplace.Id;
        }

        var normalized = domain.Trim().ToLowerInvariant();
        return normalized.All(character =>
            char.IsLetterOrDigit(character) || character == '-')
            ? normalized
            : null;
    }

    private static CooldownFile? ReadFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CooldownFile>(
                File.ReadAllText(path),
                Options);
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or JsonException
                or NotSupportedException)
        {
            return null;
        }
    }

    private static string Truncate(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var singleLine = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return singleLine.Length <= maximum
            ? singleLine
            : singleLine[..maximum];
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
            // Best effort: the process umask or a read-only filesystem may
            // reject the mode.
        }
    }

    private sealed class CooldownFile
    {
        public int Schema { get; set; } = CurrentSchema;

        public string Domain { get; set; } = string.Empty;

        public string Transport { get; set; } = "curl";

        public DateTimeOffset Until { get; set; }

        public string Reason { get; set; } = string.Empty;

        public DateTimeOffset UpdatedAt { get; set; }
    }
}
