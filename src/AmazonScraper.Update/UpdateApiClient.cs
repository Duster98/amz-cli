using System.Net;
using System.Text.Json;

namespace AmazonScraper.Update;

public sealed record ReleaseEnvelope(
    Guid ReleaseId,
    string Version,
    string Platform,
    string Channel,
    string Manifest,
    string Signature,
    string SigningKeyId,
    DateTimeOffset CreatedAt)
{
    public IReadOnlyDictionary<string, string> DownloadUrls { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed class UpdateApiException : Exception
{
    public UpdateApiException(string message, HttpStatusCode? statusCode = null)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}

public sealed class UpdateApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Uri? _feedBaseUri;

    public UpdateApiClient(
        HttpClient? http = null,
        Uri? feedBaseUri = null,
        string? currentVersion = null)
    {
        _ownsHttp = http is null;
        _http = http ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
        _feedBaseUri = feedBaseUri;

        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                $"amz-cli/{currentVersion ?? "0.0.0"}");
        }
    }

    public async Task<ReleaseEnvelope?> GetLatestAsync(
        string platform,
        string channel,
        CancellationToken cancellationToken)
    {
        var feedUri = _feedBaseUri is null
            ? new Uri(UpdateEndpoints.FeedUrl(platform, channel))
            : new Uri(_feedBaseUri, $"{platform}.json");
        if (!UpdateEndpoints.IsAllowedHost(feedUri.Host))
        {
            throw new UpdateApiException(
                $"Host del feed non consentito: '{feedUri.Host}'.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, feedUri);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        using var response = await _http
            .SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return null;
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new UpdateApiException(
                $"GitHub returned {(int)response.StatusCode} while checking for updates.",
                response.StatusCode);
        }

        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is not null
            && !UpdateEndpoints.IsAllowedHost(finalUri.Host))
        {
            throw new UpdateApiException(
                $"Redirect del feed non consentito: '{finalUri.Host}'.");
        }

        if (string.IsNullOrWhiteSpace(body)
            || string.Equals(body.Trim(), "null", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new UpdateApiException("Invalid release response.");
            }

            var schema = root.TryGetProperty("schema", out var schemaElement)
                && schemaElement.TryGetInt32(out var parsedSchema)
                    ? parsedSchema
                    : 1;
            if (schema != 1)
            {
                throw new UpdateApiException(
                    $"Schema feed non supportato: {schema}.");
            }

            var releaseId = root.GetProperty("release_id").GetGuid();
            var version = root.GetProperty("version").GetString()
                ?? throw new UpdateApiException("Release version is missing.");
            var releasePlatform = root.GetProperty("platform").GetString()
                ?? throw new UpdateApiException("Release platform is missing.");
            var releaseChannel = root.GetProperty("channel").GetString()
                ?? throw new UpdateApiException("Release channel is missing.");
            var manifest = root.GetProperty("manifest").GetString()
                ?? throw new UpdateApiException("Release manifest is missing.");
            var signature = root.GetProperty("signature").GetString()
                ?? throw new UpdateApiException("Release signature is missing.");
            var signingKeyId = root.GetProperty("signing_key_id").GetString()
                ?? throw new UpdateApiException("Signing key is missing.");
            var createdAt = root.TryGetProperty("created_at", out var createdAtElement)
                && createdAtElement.ValueKind == JsonValueKind.String
                && createdAtElement.TryGetDateTimeOffset(out var parsedCreatedAt)
                    ? parsedCreatedAt
                    : default;

            if (!string.Equals(releasePlatform, platform, StringComparison.Ordinal)
                || !string.Equals(releaseChannel, channel, StringComparison.Ordinal))
            {
                throw new UpdateApiException(
                    "The feed does not match the requested platform or channel.");
            }

            var urls = new Dictionary<string, string>(StringComparer.Ordinal);
            var urlsElement = root.GetProperty("download_urls");
            foreach (var property in urlsElement.EnumerateObject())
            {
                var url = property.Value.GetString();
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    || uri.Scheme != Uri.UriSchemeHttps
                    || !UpdateEndpoints.IsAllowedHost(uri.Host))
                {
                    throw new UpdateApiException(
                        $"URL di download non consentito: '{Shorten(url ?? string.Empty)}'.");
                }
                urls[property.Name] = url!;
            }

            if (urls.Count == 0)
            {
                throw new UpdateApiException("No downloadable files in the release.");
            }

            return new ReleaseEnvelope(
                releaseId,
                version,
                releasePlatform,
                releaseChannel,
                manifest,
                signature,
                signingKeyId,
                createdAt)
            {
                DownloadUrls = urls,
            };
        }
        catch (JsonException ex)
        {
            throw new UpdateApiException($"Invalid release JSON: {ex.Message}");
        }
        catch (KeyNotFoundException ex)
        {
            throw new UpdateApiException($"Missing release field: {ex.Message}");
        }
    }

    private static string Shorten(string value) =>
        value.Length <= 200 ? value : value[..200] + "…";

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}

internal static class UpdateJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
}
