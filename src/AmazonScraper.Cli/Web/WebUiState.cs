using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Searching;

namespace AmazonScraper.Cli.Web;

public sealed class WebSearchFilters
{
    public bool NoAds { get; set; } = true;

    public bool Delivery { get; set; } = true;

    public bool Enrich { get; set; }

    public double MinRating { get; set; }

    public double? PriceMin { get; set; }

    public double? PriceMax { get; set; }

    public bool OnlyDeals { get; set; }

    public bool FreeShipping { get; set; }

    public bool AmazonChoice { get; set; }

    public string Sort { get; set; } = "relevance";
}

public sealed class WebUiPreferences
{
    public string Theme { get; set; } = "system";

    public string Language { get; set; } = "en";

    public string DeliveryCountry { get; set; } = string.Empty;

    public string DeliveryPostalCode { get; set; } = string.Empty;

    public string Domain { get; set; } = "it";

    public List<string> Domains { get; set; } = [];

    public string DealsDomain { get; set; } = string.Empty;

    public bool RailPinned { get; set; }

    public string ResultsLayout { get; set; } = "list";

    public string MonitoringLayout { get; set; } = "grid";

    public WebSearchFilters Filters { get; set; } = new();
}

public sealed class WebRecentSearch
{
    public string Query { get; set; } = string.Empty;

    public string Domain { get; set; } = "it";

    public List<string> Domains { get; set; } = [];

    public string Marketplace { get; set; } = string.Empty;

    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Versioned UI state stored next to the web session. Preferences and recent
/// searches are typed today; the additional JSON sections reserve space for
/// saved searches, full history, comparisons and future settings without
/// changing the file format again.
/// </summary>
public sealed class WebUiState
{
    public int Schema { get; set; } = 1;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public WebUiPreferences Preferences { get; set; } = new();

    public List<WebRecentSearch> RecentSearches { get; set; } = [];

    public JsonArray SavedSearches { get; set; } = new();

    public JsonArray SearchHistory { get; set; } = new();

    public JsonArray Comparisons { get; set; } = new();

    public JsonObject Monitoring { get; set; } = new();

    public JsonObject Settings { get; set; } = new();

}

public sealed class WebRecentSearchesRequest
{
    public List<WebRecentSearch> RecentSearches { get; set; } = [];
}

public sealed class WebUiStateStore
{
    private const int CurrentSchema = 4;
    private const int MaxRecentSearches = 5;

    private static readonly HashSet<string> AllowedLanguages =
        MarketplaceRegistry.All
            .Select(item => item.Language)
            .ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> AllowedSorts =
    [
        "relevance",
        "price-asc",
        "price-desc",
        "reviews",
        "newest",
        "bestsellers",
    ];

    private static readonly HashSet<double> AllowedRatings = [0, 3, 4, 4.5];

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly object _gate = new();
    private readonly string _path;

    public WebUiStateStore(string? path = null)
    {
        _path = System.IO.Path.GetFullPath(path ?? WebPaths.UiStatePath());
    }

    public string FilePath => _path;

    public WebUiState Load()
    {
        lock (_gate)
        {
            return LoadUnlocked();
        }
    }

    public WebUiState UpdatePreferences(WebUiPreferences preferences)
    {
        lock (_gate)
        {
            var state = LoadUnlocked();
            state.Preferences = NormalizePreferences(preferences);
            state.UpdatedAt = DateTimeOffset.UtcNow;
            WriteUnlocked(state);
            return state;
        }
    }

    public WebUiState UpdateRecentSearches(
        IReadOnlyList<WebRecentSearch> recentSearches)
    {
        lock (_gate)
        {
            var state = LoadUnlocked();
            state.RecentSearches = NormalizeRecentSearches(recentSearches);
            state.UpdatedAt = DateTimeOffset.UtcNow;
            WriteUnlocked(state);
            return state;
        }
    }

    private WebUiState LoadUnlocked()
    {
        if (!File.Exists(_path))
        {
            var created = Normalize(new WebUiState());
            WriteUnlocked(created);
            return created;
        }

        try
        {
            var state = JsonSerializer.Deserialize<WebUiState>(
                File.ReadAllText(_path),
                Options);
            var needsWrite = state is null || state.Schema != CurrentSchema;
            var normalized = Normalize(state ?? new WebUiState());
            if (needsWrite)
            {
                WriteUnlocked(normalized);
            }
            return normalized;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or JsonException
                or NotSupportedException)
        {
            // A damaged file must not prevent the UI from starting.
            var recovered = Normalize(new WebUiState());
            WriteUnlocked(recovered);
            return recovered;
        }
    }

    private static WebUiState Normalize(WebUiState state)
    {
        state.Schema = CurrentSchema;
        state.UpdatedAt = state.UpdatedAt == default
            ? DateTimeOffset.UtcNow
            : state.UpdatedAt;
        state.Preferences = NormalizePreferences(state.Preferences);
        state.RecentSearches = NormalizeRecentSearches(state.RecentSearches);
        state.SavedSearches ??= new JsonArray();
        state.SearchHistory ??= new JsonArray();
        state.Comparisons ??= new JsonArray();
        state.Monitoring ??= new JsonObject();
        state.Settings ??= new JsonObject();
        return state;
    }

    private static WebUiPreferences NormalizePreferences(WebUiPreferences? preferences)
    {
        var source = preferences ?? new WebUiPreferences();
        var theme = (source.Theme ?? string.Empty).Trim().ToLowerInvariant();
        if (theme is not ("system" or "light" or "dark"))
        {
            theme = "system";
        }

        var language = (source.Language ?? string.Empty).Trim().ToLowerInvariant();
        if (!AllowedLanguages.Contains(language))
        {
            language = "en";
        }

        var domains = NormalizeDomains(
            source.Domains,
            source.Domain ?? MarketplaceRegistry.Default.Id);
        var domain = domains[0];
        var primaryMarketplace = MarketplaceRegistry.TryResolve(
            domain,
            out var resolvedMarketplace)
            ? resolvedMarketplace
            : MarketplaceRegistry.Default;

        var deliveryCountry = (source.DeliveryCountry ?? string.Empty)
            .Trim()
            .ToUpperInvariant();
        var deliveryPostalCode = (source.DeliveryPostalCode ?? string.Empty)
            .Trim();
        if (deliveryPostalCode.Length == 0)
        {
            if (deliveryCountry.Length == 0
                || !primaryMarketplace.SupportsCountryOnlyDelivery
                || string.Equals(
                    deliveryCountry,
                    primaryMarketplace.CountryCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                deliveryCountry = string.Empty;
            }
        }
        else
        {
            var requestedCountry = deliveryCountry.Length == 0
                ? primaryMarketplace.CountryCode
                : deliveryCountry;
            if (!PostalCodeValidator.TryNormalize(
                    requestedCountry,
                    deliveryPostalCode,
                    out deliveryCountry,
                    out deliveryPostalCode,
                    out _))
            {
                deliveryCountry = string.Empty;
                deliveryPostalCode = string.Empty;
            }
            else if (primaryMarketplace.SupportsCountryOnlyDelivery
                     && !string.Equals(
                         deliveryCountry,
                         primaryMarketplace.CountryCode,
                         StringComparison.OrdinalIgnoreCase))
            {
                // International destinations on .com/.jp are country-only.
                deliveryPostalCode = string.Empty;
            }
        }

        var dealsDomain = MarketplaceRegistry.TryResolve(
            source.DealsDomain,
            out var dealsMarketplace)
            ? dealsMarketplace.Id
            : domain;
        var layout = string.Equals(
            source.ResultsLayout,
            "grid",
            StringComparison.OrdinalIgnoreCase)
            ? "grid"
            : "list";
        var monitoringLayout = string.Equals(
            source.MonitoringLayout,
            "list",
            StringComparison.OrdinalIgnoreCase)
            ? "list"
            : "grid";

        var minRating = AllowedRatings.Contains(source.Filters?.MinRating ?? 0)
            ? source.Filters?.MinRating ?? 0
            : 0;
        var sort = (source.Filters?.Sort ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        if (!AllowedSorts.Contains(sort))
        {
            sort = "relevance";
        }

        var priceMin = NormalizePrice(source.Filters?.PriceMin);
        var priceMax = NormalizePrice(source.Filters?.PriceMax);
        if (priceMin is not null && priceMax is not null && priceMin > priceMax)
        {
            (priceMin, priceMax) = (priceMax, priceMin);
        }

        return new WebUiPreferences
        {
            Theme = theme,
            Language = language,
            DeliveryCountry = deliveryCountry,
            DeliveryPostalCode = deliveryPostalCode,
            Domain = domain,
            Domains = domains,
            DealsDomain = dealsDomain,
            RailPinned = source.RailPinned,
            ResultsLayout = layout,
            MonitoringLayout = monitoringLayout,
            Filters = new WebSearchFilters
            {
                NoAds = source.Filters?.NoAds ?? true,
                Delivery = source.Filters?.Delivery ?? true,
                Enrich = source.Filters?.Enrich ?? false,
                MinRating = minRating,
                PriceMin = priceMin,
                PriceMax = priceMax,
                OnlyDeals = source.Filters?.OnlyDeals ?? false,
                FreeShipping = source.Filters?.FreeShipping ?? false,
                AmazonChoice = source.Filters?.AmazonChoice ?? false,
                Sort = sort,
            },
        };
    }

    private static List<string> NormalizeDomains(
        IEnumerable<string>? domains,
        string fallback)
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in domains ?? [])
        {
            if (string.IsNullOrWhiteSpace(candidate)
                || !MarketplaceRegistry.TryResolve(candidate, out var marketplace)
                || !seen.Add(marketplace.Id))
            {
                continue;
            }

            results.Add(marketplace.Id);
        }

        if (results.Count == 0
            && MarketplaceRegistry.TryResolve(fallback, out var fallbackMarketplace))
        {
            results.Add(fallbackMarketplace.Id);
        }

        if (results.Count == 0)
        {
            results.Add(MarketplaceRegistry.Default.Id);
        }

        return results;
    }

    private static double? NormalizePrice(double? value)
    {
        if (value is null or < 0 or > 1_000_000)
        {
            return null;
        }

        return Math.Round(value.Value, 2);
    }

    private static List<WebRecentSearch> NormalizeRecentSearches(
        IEnumerable<WebRecentSearch>? recentSearches)
    {
        var results = new List<WebRecentSearch>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in (recentSearches ?? []).OrderByDescending(item => item.At))
        {
            var query = (item.Query ?? string.Empty).Trim();
            if (query.Length == 0 || query.Length > 200)
            {
                continue;
            }
            if (!MarketplaceRegistry.TryResolve(item.Domain, out var marketplace))
            {
                continue;
            }
            var domains = NormalizeDomains(item.Domains, marketplace.Id);
            var domain = domains[0];
            var key = $"{domain}:{query.ToLowerInvariant()}";
            if (!seen.Add(key))
            {
                continue;
            }

            results.Add(new WebRecentSearch
            {
                Query = query,
                Domain = domain,
                Domains = domains,
                Marketplace = string.IsNullOrWhiteSpace(item.Marketplace)
                    ? marketplace.DisplayName
                    : item.Marketplace.Trim(),
                At = item.At == default ? DateTimeOffset.UtcNow : item.At,
            });
            if (results.Count >= MaxRecentSearches)
            {
                break;
            }
        }

        return results;
    }

    private void WriteUnlocked(WebUiState state)
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
            JsonSerializer.Serialize(state, Options),
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
            // Best effort: the process umask or a read-only filesystem may
            // reject the mode.
        }
    }
}
