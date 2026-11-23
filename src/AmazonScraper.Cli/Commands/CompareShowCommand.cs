using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using AmazonScraper.Cli.Rendering;
using AmazonScraper.Cli.Services;
using AmazonScraper.Cli.Storage;
using AmazonScraper.Cli.Web;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Commands;

public sealed class CompareShowSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("JSON output")]
    public bool Json { get; set; }

    [CommandOption("--tk-opt")]
    [Description("Tab-separated output")]
    public bool TkOpt { get; set; }

    [CommandOption("--diff")]
    [Description("Transpose the table and mark differing attributes and specifications")]
    public bool Diff { get; set; }

    [CommandOption("--refresh")]
    [Description("Refresh product details instead of using the product cache")]
    public bool Refresh { get; set; }

    [CommandOption("--no-buying-options")]
    [Description("Skip the buying-options request (faster, less data)")]
    public bool NoBuyingOptions { get; set; }

    [CommandOption("--timeout <SECONDS>")]
    [Description("HTTP timeout in seconds (default: saved setting)")]
    public double? Timeout { get; set; }

    [CommandOption("--country <ISO>")]
    [Description("Two-letter ISO delivery country for every product")]
    public string? Country { get; set; }

    [CommandOption("--cap <POSTAL>")]
    [Description("Delivery postal code for every product")]
    public string? Cap { get; set; }

    [CommandOption("--width <COLUMNS>")]
    [Description("Table width (default 200)")]
    [DefaultValue(200)]
    public int Width { get; set; } = 200;

    public override ValidationResult Validate()
    {
        if (Width is < 40 or > 400)
        {
            return ValidationResult.Error(
                "--width must be between 40 and 400.");
        }
        if (Timeout is <= 0)
        {
            return ValidationResult.Error(
                "--timeout must be greater than zero.");
        }
        if (Country is not null
            && !AmazonScraper.Core.Searching.PostalCodeValidator
                .IsCountryCode(Country))
        {
            return ValidationResult.Error(
                "--country must be a two-letter ISO code (e.g. IT, FR, GB, ES).");
        }
        return ValidationResult.Success();
    }
}

public sealed class CompareShowCommand : AsyncCommand<CompareShowSettings>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly CompareStore _store;

    public CompareShowCommand()
        : this(new CompareStore())
    {
    }

    internal CompareShowCommand(CompareStore store)
    {
        _store = store;
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        CompareShowSettings settings,
        CancellationToken cancellationToken)
    {
        var document = _store.Read();
        if (document.Items.Count == 0)
        {
            if (settings.Json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(
                    new
                    {
                        comparison = new
                        {
                            revision = document.Revision,
                            updated_at = document.UpdatedAt,
                        },
                        items = Array.Empty<object>(),
                    },
                    JsonOptions));
            }
            else
            {
                Console.Out.WriteLine(
                    "The comparison is empty. Add products with " +
                    "'amz-cli compare add'.");
            }
            return 0;
        }

        var savedHttp = HttpSettingsStore.Default.Load();
        var service = new ProductDetailsService();
        var results = new List<CompareShowItem>();
        foreach (var item in document.Items)
        {
            try
            {
                var result = await service
                    .GetAsync(
                        new ProductDetailsRequest(
                            item.Asin,
                            item.Domain,
                            IncludeRating: true,
                            IncludeReviews: true,
                            IncludeBuyingOptions: !settings.NoBuyingOptions,
                            OptionsDelayMs: 1500,
                            DeliveryCountry: settings.Country,
                            DeliveryPostalCode: settings.Cap,
                            TimeoutSeconds: settings.Timeout
                                ?? savedHttp.TimeoutSeconds,
                            Http: savedHttp),
                        cancellationToken,
                        settings.Refresh)
                    .ConfigureAwait(false);
                results.Add(new CompareShowItem(
                    item,
                    result.Product,
                    result.Currency,
                    null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(new CompareShowItem(
                    item,
                    null,
                    string.Empty,
                    ex.Message));
                CliConsole.Error($"Unable to load {item.Id}: {ex.Message}");
            }
        }

        var mode = OutputModeSelector.Pick(settings.Json, settings.TkOpt);
        var diff = settings.Diff
            ? CompareDiffBuilder.Build(results)
            : null;

        switch (mode)
        {
            case OutputMode.Json:
                Console.Out.WriteLine(JsonSerializer.Serialize(
                    settings.Diff && diff is not null
                        ? DiffPayload(document, diff)
                        : ItemsPayload(document, results),
                    JsonOptions));
                break;
            case OutputMode.Tsv:
                Console.Out.WriteLine(
                    settings.Diff && diff is not null
                        ? CompareTabsTsv.Diff(diff)
                        : CompareTabsTsv.Table(results));
                break;
            default:
                var console = CliConsole.Stdout(settings.Width);
                if (settings.Diff && diff is not null)
                {
                    CompareTabsRenderer.RenderDiff(console, diff);
                }
                else
                {
                    CompareTabsRenderer.RenderTable(console, results);
                }
                break;
        }

        return results.All(item => item.Error is not null) ? 1 : 0;
    }

    private static object ItemsPayload(
        CompareDocument document,
        IReadOnlyList<CompareShowItem> results) =>
        new
        {
            comparison = new
            {
                revision = document.Revision,
                updated_at = document.UpdatedAt,
            },
            items = results.Select(item => new
            {
                market = item.Item.Domain,
                asin = item.Item.Asin,
                source = item.Item.Source,
                added_at = item.Item.AddedAt,
                currency = item.Currency,
                error = item.Error,
                product = item.Detail,
            }),
        };

    private static object DiffPayload(
        CompareDocument document,
        CompareDiff diff) =>
        new
        {
            comparison = new
            {
                revision = document.Revision,
                updated_at = document.UpdatedAt,
            },
            products = diff.Products,
            attributes = diff.Attributes,
            difference_count = diff.DifferenceCount,
            attribute_count = diff.Attributes.Count,
        };
}
