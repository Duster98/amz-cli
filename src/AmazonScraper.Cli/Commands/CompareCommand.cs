using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AmazonScraper.Cli.Rendering;
using AmazonScraper.Cli.Web;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Commands;

public sealed class CompareAddSettings : CommandSettings
{
    [CommandArgument(0, "<PRODUCTS>")]
    [Description("ASIN, domain:ASIN, or Amazon URL")]
    public string[] Products { get; set; } = [];

    [CommandOption("--domain <DOMAIN>")]
    [Description("Default marketplace for bare ASINs (default: it)")]
    public string? Domain { get; set; }

    [CommandOption("--source <SOURCE>")]
    [Description("Origin stored with the items: cli, agent, or web")]
    [DefaultValue("cli")]
    public string Source { get; set; } = "cli";

    [CommandOption("--json")]
    [Description("JSON output")]
    public bool Json { get; set; }

    public override ValidationResult Validate()
    {
        if (Products.Length == 0)
        {
            return ValidationResult.Error("Specify at least one product.");
        }
        if (Source is not ("cli" or "agent" or "web"))
        {
            return ValidationResult.Error(
                "--source must be cli, agent, or web.");
        }
        return ValidationResult.Success();
    }
}

public sealed class CompareListSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("JSON output")]
    public bool Json { get; set; }
}

public sealed class CompareRemoveSettings : CommandSettings
{
    [CommandArgument(0, "<PRODUCTS>")]
    [Description("ASIN, domain:ASIN, or Amazon URL")]
    public string[] Products { get; set; } = [];

    [CommandOption("--domain <DOMAIN>")]
    [Description("Default marketplace for bare ASINs (default: it)")]
    public string? Domain { get; set; }

    [CommandOption("--json")]
    [Description("JSON output")]
    public bool Json { get; set; }

    public override ValidationResult Validate() =>
        Products.Length == 0
            ? ValidationResult.Error("Specify at least one product.")
            : ValidationResult.Success();
}

public sealed class CompareClearSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("JSON output")]
    public bool Json { get; set; }
}

public sealed class CompareAddCommand : Command<CompareAddSettings>
{
    private readonly CompareStore _store;

    public CompareAddCommand()
        : this(new CompareStore())
    {
    }

    internal CompareAddCommand(CompareStore store)
    {
        _store = store;
    }

    public override int Execute(
        CommandContext context,
        CompareAddSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var targets = settings.Products
                .Select(value => LibraryCommandSupport.ParseTarget(
                    value,
                    settings.Domain))
                .Select(target => new CompareTarget(
                    target.Domain,
                    CompareStore.NormalizeAsin(target.Asin)))
                .ToArray();
            var document = _store.Add(targets, settings.Source);
            if (settings.Json)
            {
                Console.Out.WriteLine(
                    JsonSerializer.Serialize(document, JsonOptions));
            }
            else
            {
                Console.Out.WriteLine(
                    $"Comparison: {document.Items.Count} of {CompareStore.MaxItems} products.");
                foreach (var item in document.Items)
                {
                    Console.Out.WriteLine($"  {item.Id}  ({item.Source})");
                }
            }
            return 0;
        }
        catch (Exception ex) when (
            ex is CompareException
                or AmazonScraper.Core.Marketplaces.MarketplaceException
                or AmazonScraper.Core.Monitoring.MonitorException
                or ArgumentException)
        {
            CliConsole.Error(ex.Message);
            return 1;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public sealed class CompareListCommand : Command<CompareListSettings>
{
    private readonly CompareStore _store;

    public CompareListCommand()
        : this(new CompareStore())
    {
    }

    internal CompareListCommand(CompareStore store)
    {
        _store = store;
    }

    public override int Execute(
        CommandContext context,
        CompareListSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var document = _store.Read();
            if (settings.Json)
            {
                Console.Out.WriteLine(
                    JsonSerializer.Serialize(document, JsonOptions));
                return 0;
            }
            if (document.Items.Count == 0)
            {
                Console.Out.WriteLine("The comparison is empty.");
                return 0;
            }

            var table = new Table()
                .Border(TableBorder.HeavyHead)
                .BorderStyle(new Style(Color.Cyan));
            table.AddColumn(new TableColumn(new Text("#")));
            table.AddColumn(new TableColumn(new Text("Product")));
            table.AddColumn(new TableColumn(new Text("Source")));
            table.AddColumn(new TableColumn(new Text("Added")));
            for (var index = 0; index < document.Items.Count; index++)
            {
                var item = document.Items[index];
                table.AddRow(
                    (index + 1).ToString(CultureInfo.InvariantCulture),
                    item.Id,
                    item.Source,
                    item.AddedAt.ToLocalTime()
                        .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            }
            AnsiConsole.Write(table);
            return 0;
        }
        catch (CompareException ex)
        {
            CliConsole.Error(ex.Message);
            return 1;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public sealed class CompareRemoveCommand : Command<CompareRemoveSettings>
{
    private readonly CompareStore _store;

    public CompareRemoveCommand()
        : this(new CompareStore())
    {
    }

    internal CompareRemoveCommand(CompareStore store)
    {
        _store = store;
    }

    public override int Execute(
        CommandContext context,
        CompareRemoveSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            CompareDocument document = _store.Read();
            foreach (var value in settings.Products)
            {
                var target = LibraryCommandSupport.ParseTarget(
                    value,
                    settings.Domain);
                document = _store.Remove(target.Domain, target.Asin);
            }
            if (settings.Json)
            {
                Console.Out.WriteLine(
                    JsonSerializer.Serialize(document, JsonOptions));
            }
            else
            {
                Console.Out.WriteLine(
                    $"Comparison: {document.Items.Count} of {CompareStore.MaxItems} products.");
            }
            return 0;
        }
        catch (Exception ex) when (
            ex is CompareException
                or AmazonScraper.Core.Marketplaces.MarketplaceException
                or AmazonScraper.Core.Monitoring.MonitorException
                or ArgumentException)
        {
            CliConsole.Error(ex.Message);
            return 1;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public sealed class CompareClearCommand : Command<CompareClearSettings>
{
    private readonly CompareStore _store;

    public CompareClearCommand()
        : this(new CompareStore())
    {
    }

    internal CompareClearCommand(CompareStore store)
    {
        _store = store;
    }

    public override int Execute(
        CommandContext context,
        CompareClearSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var document = _store.Clear();
            if (settings.Json)
            {
                Console.Out.WriteLine(
                    JsonSerializer.Serialize(document, JsonOptions));
            }
            else
            {
                Console.Out.WriteLine("Comparison cleared.");
            }
            return 0;
        }
        catch (CompareException ex)
        {
            CliConsole.Error(ex.Message);
            return 1;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
