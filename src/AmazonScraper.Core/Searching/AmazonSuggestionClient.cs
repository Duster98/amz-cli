using System.Net;
using System.Text.Json;
using AmazonScraper.Core.Marketplaces;

namespace AmazonScraper.Core.Searching;

/// <summary>
/// Reads the JSON autocomplete endpoint used by the Amazon search box.
/// This is intentionally independent from <see cref="Http.AmazonClient"/>:
/// the endpoint is public, does not need a warm-up or cookies, and should not
/// be slowed down or blocked by the storefront anti-bot flow.
/// </summary>
public sealed class AmazonSuggestionClient
{
    private const int MaxPrefixLength = 80;
    private const int MaxSuggestions = 10;

    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    private readonly HttpClient _http;

    public AmazonSuggestionClient(HttpClient? httpClient = null)
    {
        _http = httpClient ?? SharedHttpClient;
    }

    public async Task<IReadOnlyList<string>> GetAsync(
        IMarketplaceProfile marketplace,
        string prefix,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizePrefix(prefix);
        if (normalized.Length == 0
            || !SuggestionCatalog.TryGet(marketplace.Id, out var settings))
        {
            return [];
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            settings.SuggestionsUrl(normalized));
        request.Headers.TryAddWithoutValidation(
            "Accept",
            "application/json, text/javascript, */*; q=0.01");
        request.Headers.TryAddWithoutValidation(
            "Accept-Language",
            marketplace.AcceptLanguage);
        request.Headers.TryAddWithoutValidation("Referer", marketplace.BaseUri.ToString());
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/131.0.0.0 Safari/537.36");

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Suggestions unavailable: HTTP {(int)response.StatusCode}.");
        }

        var json = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        return Parse(
            json,
            normalized,
            MaxSuggestions);
    }

    internal static IReadOnlyList<string> Parse(
        string json,
        string prefix,
        int limit = MaxSuggestions)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(
                    "suggestions",
                    out var suggestions)
                || suggestions.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var results = new List<string>(Math.Min(limit, MaxSuggestions));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in suggestions.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || (item.TryGetProperty("type", out var type)
                        && type.ValueKind == JsonValueKind.String
                        && !string.Equals(
                            type.GetString(),
                            "KEYWORD",
                            StringComparison.OrdinalIgnoreCase))
                    || (item.TryGetProperty("ghost", out var ghost)
                        && ghost.ValueKind == JsonValueKind.True))
                {
                    continue;
                }

                var value = item.TryGetProperty("value", out var raw)
                    && raw.ValueKind == JsonValueKind.String
                    ? WebUtility.HtmlDecode(raw.GetString() ?? string.Empty).Trim()
                    : string.Empty;
                if (value.Length == 0
                    || value.Length > 120
                    || string.Equals(
                        value,
                        prefix.Trim(),
                        StringComparison.OrdinalIgnoreCase)
                    || !seen.Add(value))
                {
                    continue;
                }

                results.Add(value);
                if (results.Count >= Math.Clamp(limit, 1, MaxSuggestions))
                {
                    break;
                }
            }

            return results;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string NormalizePrefix(string? prefix)
    {
        var value = (prefix ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return string.Empty;
        }

        var characters = value
            .Where(character => !char.IsControl(character))
            .ToArray();
        var normalized = new string(characters).Trim();
        return normalized.Length > MaxPrefixLength
            ? normalized[..MaxPrefixLength].TrimEnd()
            : normalized;
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
    }
}
