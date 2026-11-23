using System.Globalization;
using System.Text.Json;
using AmazonScraper.Cli.Library;
using AmazonScraper.Cli.Monitoring;
using AmazonScraper.Cli.Rendering;
using AmazonScraper.Cli.Storage;
using AmazonScraper.Core.Formatting;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Monitoring;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Commands;

public sealed class LibraryListCommand : Command<LibraryListSettings>
{
    public override int Execute(
        CommandContext context,
        LibraryListSettings settings,
        CancellationToken cancellationToken)
    {
        return LibraryCommandSupport.Run(() =>
        {
            var document = new SavedLibraryStore().Read();
            var products = document.Products;
            SavedGroup? selectedGroup = null;
            if (!string.IsNullOrWhiteSpace(settings.Group))
            {
                selectedGroup = LibraryCommandSupport.ResolveGroup(
                    document,
                    settings.Group);
                var ids = selectedGroup.Items
                    .Select(item => item.ProductId)
                    .ToHashSet(StringComparer.Ordinal);
                products = products
                    .Where(product => ids.Contains(product.Id))
                    .ToList();
            }

            var groups = selectedGroup is null
                ? document.Groups
                : [selectedGroup];
            LibraryOutput.Products(
                LibraryOutput.FilteredDocument(document, products, groups),
                products,
                OutputModeSelector.Pick(settings.Json, settings.TkOpt),
                settings.Width);
            return 0;
        });
    }
}

public sealed class LibraryShowCommand : Command<LibraryShowSettings>
{
    public override int Execute(
        CommandContext context,
        LibraryShowSettings settings,
        CancellationToken cancellationToken)
    {
        return LibraryCommandSupport.Run(() =>
        {
            var document = new SavedLibraryStore().Read();
            var target = LibraryCommandSupport.ParseTarget(
                settings.Product,
                settings.Domain);
            var product = LibraryCommandSupport.FindProduct(
                document,
                target);
            var groups = document.Groups
                .Where(group => group.Items.Any(item =>
                    string.Equals(
                        item.ProductId,
                        product.Id,
                        StringComparison.Ordinal)))
                .ToList();
            LibraryOutput.Products(
                LibraryOutput.FilteredDocument(document, [product], groups),
                [product],
                OutputModeSelector.Pick(settings.Json, settings.TkOpt),
                settings.Width);
            return 0;
        });
    }
}

public sealed class LibraryAddCommand : AsyncCommand<LibraryAddSettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        LibraryAddSettings settings,
        CancellationToken cancellationToken)
    {
        var store = new SavedLibraryStore();
        SavedGroup? group = null;
        var targets = new List<MonitorTarget>();
        try
        {
            var document = store.Read();
            if (!string.IsNullOrWhiteSpace(settings.Group))
            {
                group = LibraryCommandSupport.ResolveGroup(
                    document,
                    settings.Group);
            }
            foreach (var value in settings.Products)
            {
                targets.Add(LibraryCommandSupport.ParseTarget(
                    value,
                    settings.Domain));
            }
        }
        catch (Exception ex) when (
            ex is SavedLibraryException
                or MonitorException
                or MarketplaceException
                or ArgumentException)
        {
            CliConsole.Error(ex.Message);
            return 1;
        }

        var resolver = new SavedProductResolver();
        var savedHttp = HttpSettingsStore.Default.Load();
        var timeoutSeconds = settings.Timeout ?? savedHttp.TimeoutSeconds;
        var resolved = new List<SavedProduct>();
        var failed = false;
        foreach (var target in targets.Distinct())
        {
            try
            {
                resolved.Add(await resolver
                    .ResolveAsync(
                        target,
                        timeoutSeconds,
                        savedHttp,
                        cancellationToken)
                    .ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed = true;
                CliConsole.Error(
                    $"Unable to save {target}: {ex.Message}");
            }
        }

        if (resolved.Count == 0)
        {
            return 1;
        }

        return LibraryCommandSupport.Run(() =>
        {
            var document = store.AddOrUpdateProducts(resolved);
            if (group is not null)
            {
                document = store.AddGroupItems(
                    group.Id,
                    resolved
                        .Select(product => new SavedGroupItem
                        {
                            ProductId = product.Id,
                            Quantity = settings.Quantity,
                        }));
            }

            var ids = resolved
                .Select(product => product.Id)
                .ToHashSet(StringComparer.Ordinal);
            var added = document.Products
                .Where(product => ids.Contains(product.Id))
                .ToList();
            LibraryOutput.Products(
                document,
                added,
                OutputModeSelector.Pick(settings.Json, settings.TkOpt),
                settings.Width);
            return failed ? 1 : 0;
        });
    }
}

public sealed class LibraryRemoveCommand : Command<LibraryRemoveSettings>
{
    public override int Execute(
        CommandContext context,
        LibraryRemoveSettings settings,
        CancellationToken cancellationToken)
    {
        return LibraryCommandSupport.Run(() =>
        {
            var store = new SavedLibraryStore();
            var document = store.Read();
            var targets = settings.Products
                .Select(value => LibraryCommandSupport.ParseTarget(
                    value,
                    settings.Domain))
                .Distinct()
                .ToList();
            var removed = new List<SavedProduct>();
            foreach (var target in targets)
            {
                var product = LibraryCommandSupport.FindProduct(
                    document,
                    target,
                    required: false);
                if (product is null)
                {
                    CliConsole.Error(
                        $"Product not saved: {target}.");
                    continue;
                }
                removed.Add(product);
                document = store.RemoveProduct(product.Id);
            }

            if (removed.Count == 0)
            {
                return 1;
            }

            LibraryOutput.Products(
                document,
                removed,
                OutputModeSelector.Pick(settings.Json, settings.TkOpt),
                settings.Width);
            return removed.Count == targets.Count ? 0 : 1;
        });
    }
}

public sealed class LibraryGroupListCommand : Command<LibraryGroupListSettings>
{
    public override int Execute(
        CommandContext context,
        LibraryGroupListSettings settings,
        CancellationToken cancellationToken)
    {
        return LibraryCommandSupport.Run(() =>
        {
            var document = new SavedLibraryStore().Read();
            LibraryOutput.Groups(
                document,
                document.Groups,
                OutputModeSelector.Pick(settings.Json, settings.TkOpt),
                settings.Width);
            return 0;
        });
    }
}

public sealed class LibraryGroupShowCommand : Command<LibraryGroupShowSettings>
{
    public override int Execute(
        CommandContext context,
        LibraryGroupShowSettings settings,
        CancellationToken cancellationToken)
    {
        return LibraryCommandSupport.Run(() =>
        {
            var document = new SavedLibraryStore().Read();
            var group = LibraryCommandSupport.ResolveGroup(
                document,
                settings.Group);
            var products = LibraryCommandSupport.GroupProducts(document, group);
            LibraryOutput.Group(
                LibraryOutput.FilteredDocument(document, products, [group]),
                group,
                OutputModeSelector.Pick(settings.Json, settings.TkOpt),
                settings.Width);
            return 0;
        });
    }
}

public sealed class LibraryGroupCreateCommand : Command<LibraryGroupCreateSettings>
{
    public override int Execute(
        CommandContext context,
        LibraryGroupCreateSettings settings,
        CancellationToken cancellationToken)
    {
        return LibraryCommandSupport.Run(() =>
        {
            var store = new SavedLibraryStore();
            var group = new SavedGroup
            {
                Id = SavedLibraryNormalizer.NewGroupId(),
                Name = settings.Name,
                Accent = settings.Accent,
                Description = settings.Description,
            };
            var document = store.CreateGroup(group);
            var created = LibraryCommandSupport.ResolveGroup(
                document,
                group.Id);
            LibraryOutput.Groups(
                document,
                [created],
                OutputModeSelector.Pick(settings.Json, settings.TkOpt),
                settings.Width);
            return 0;
        });
    }
}

public sealed class LibraryGroupRenameCommand : Command<LibraryGroupRenameSettings>
{
    public override int Execute(
        CommandContext context,
        LibraryGroupRenameSettings settings,
        CancellationToken cancellationToken)
    {
        return LibraryCommandSupport.Run(() =>
        {
            var store = new SavedLibraryStore();
            var document = store.Read();
            var group = LibraryCommandSupport.ResolveGroup(
                document,
                settings.Group);
            var accent = settings.Accent ?? group.Accent;
            document = store.UpdateGroup(
                group.Id,
                settings.Name,
                accent,
                settings.Description);
            var updated = LibraryCommandSupport.ResolveGroup(
                document,
                group.Id);
            LibraryOutput.Groups(
                document,
                [updated],
                OutputModeSelector.Pick(settings.Json, settings.TkOpt),
                settings.Width);
            return 0;
        });
    }
}

public sealed class LibraryGroupEditCommand : Command<LibraryGroupEditSettings>
{
    public override int Execute(
        CommandContext context,
        LibraryGroupEditSettings settings,
        CancellationToken cancellationToken)
    {
        return LibraryCommandSupport.Run(() =>
        {
            var store = new SavedLibraryStore();
            var document = store.Read();
            var group = LibraryCommandSupport.ResolveGroup(
                document,
                settings.Group);
            var description = settings.ClearDescription
                ? string.Empty
                : settings.Description;
            document = store.UpdateGroup(
                group.Id,
                settings.Name ?? group.Name,
                settings.Accent ?? group.Accent,
                description);
            var updated = LibraryCommandSupport.ResolveGroup(
                document,
                group.Id);
            LibraryOutput.Groups(
                document,
                [updated],
                OutputModeSelector.Pick(settings.Json, settings.TkOpt),
                settings.Width);
            return 0;
        });
    }
}

public sealed class LibraryGroupDeleteCommand : Command<LibraryGroupDeleteSettings>
{
    public override int Execute(
        CommandContext context,
        LibraryGroupDeleteSettings settings,
        CancellationToken cancellationToken)
    {
        return LibraryCommandSupport.Run(() =>
        {
            var store = new SavedLibraryStore();
            var document = store.Read();
            var group = LibraryCommandSupport.ResolveGroup(
                document,
                settings.Group);
            document = store.DeleteGroup(group.Id);
            LibraryOutput.Groups(
                document,
                document.Groups,
                OutputModeSelector.Pick(settings.Json, settings.TkOpt),
                settings.Width);
            return 0;
        });
    }
}

public sealed class LibraryGroupAddCommand : Command<LibraryGroupAddSettings>
{
    public override int Execute(
        CommandContext context,
        LibraryGroupAddSettings settings,
        CancellationToken cancellationToken)
    {
        return LibraryCommandSupport.Run(() =>
        {
            var store = new SavedLibraryStore();
            var document = store.Read();
            var group = LibraryCommandSupport.ResolveGroup(
                document,
                settings.Group);
            var products = settings.Products
                .Select(value => LibraryCommandSupport.ParseTarget(
                    value,
                    settings.Domain))
                .Distinct()
                .Select(target => LibraryCommandSupport.FindProduct(
                    document,
                    target))
                .ToList();
            document = store.AddGroupItems(
                group.Id,
                products.Select(product => new SavedGroupItem
                {
                    ProductId = product.Id,
                    Quantity = settings.Quantity,
                }));
            var updated = LibraryCommandSupport.ResolveGroup(
                document,
                group.Id);
            LibraryOutput.Group(
                LibraryOutput.FilteredDocument(
                    document,
                    LibraryCommandSupport.GroupProducts(document, updated),
                    [updated]),
                updated,
                OutputModeSelector.Pick(settings.Json, settings.TkOpt),
                settings.Width);
            return 0;
        });
    }
}

public sealed class LibraryGroupRemoveCommand : Command<LibraryGroupRemoveSettings>
{
    public override int Execute(
        CommandContext context,
        LibraryGroupRemoveSettings settings,
        CancellationToken cancellationToken)
    {
        return LibraryCommandSupport.Run(() =>
        {
            var store = new SavedLibraryStore();
            var document = store.Read();
            var group = LibraryCommandSupport.ResolveGroup(
                document,
                settings.Group);
            var failed = false;
            foreach (var value in settings.Products)
            {
                var target = LibraryCommandSupport.ParseTarget(
                    value,
                    settings.Domain);
                var productId = $"{target.Domain}:{target.Asin}";
                var current = LibraryCommandSupport.ResolveGroup(
                    document,
                    group.Id);
                if (!current.Items.Any(item =>
                        string.Equals(
                            item.ProductId,
                            productId,
                            StringComparison.Ordinal)))
                {
                    failed = true;
                    CliConsole.Error(
                        $"Product {productId} is not in group " +
                        $"'{current.Name}'.");
                    continue;
                }
                document = store.RemoveGroupItem(group.Id, productId);
            }
            var updated = LibraryCommandSupport.ResolveGroup(
                document,
                group.Id);
            LibraryOutput.Group(
                LibraryOutput.FilteredDocument(
                    document,
                    LibraryCommandSupport.GroupProducts(document, updated),
                    [updated]),
                updated,
                OutputModeSelector.Pick(settings.Json, settings.TkOpt),
                settings.Width);
            return failed ? 1 : 0;
        });
    }
}

public sealed class LibraryImportMonitorCommand
    : Command<LibraryImportMonitorSettings>
{
    public override int Execute(
        CommandContext context,
        LibraryImportMonitorSettings settings,
        CancellationToken cancellationToken)
    {
        return LibraryCommandSupport.Run(() =>
        {
            var config = MonitorConfigLoader.Load(settings.Config);
            var defaultDomain = string.IsNullOrWhiteSpace(settings.Domain)
                ? config.Domain ?? MarketplaceRegistry.Default.Id
                : settings.Domain;
            var result = MonitorImportService.Import(
                config,
                defaultDomain,
                new SavedLibraryStore(),
                new MonitorBindingStore(),
                settings.DryRun);
            Output(result, settings);
            return 0;
        });
    }

    private static void Output(
        MonitorImportResult result,
        LibraryImportMonitorSettings settings)
    {
        var mode = OutputModeSelector.Pick(settings.Json, settings.TkOpt);
        if (mode == OutputMode.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                result,
                MonitorBindingStore.JsonOptions));
            return;
        }

        if (mode == OutputMode.Tsv)
        {
            Console.WriteLine(
                "products_added\tgroups_imported\tproduct_bindings\tgroup_bindings");
            Console.WriteLine(string.Join('\t',
                result.ProductsAdded,
                result.GroupsImported,
                result.ProductBindings,
                result.GroupBindings));
        }
        else
        {
            Console.WriteLine($"Products added: {result.ProductsAdded}");
            Console.WriteLine($"Groups imported: {result.GroupsImported}");
            Console.WriteLine($"Product bindings: {result.ProductBindings}");
            Console.WriteLine($"Group bindings: {result.GroupBindings}");
        }

        foreach (var warning in result.Warnings)
        {
            CliConsole.Error($"Warning: {warning}");
        }
    }
}

internal static class LibraryCommandSupport
{
    public static int Run(Func<int> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (
            ex is SavedLibraryException
                or MonitorException
                or MarketplaceException
                or ArgumentException)
        {
            CliConsole.Error(ex.Message);
            return 1;
        }
    }

    public static MonitorTarget ParseTarget(string value, string? domain)
    {
        var defaultDomain = string.IsNullOrWhiteSpace(domain)
            ? MarketplaceRegistry.Default.Id
            : domain;
        return MonitorTargetParser.Parse(value, defaultDomain);
    }

    public static SavedGroup ResolveGroup(
        SavedLibraryDocument document,
        string reference)
    {
        var value = reference.Trim();
        var byId = document.Groups.FirstOrDefault(group =>
            string.Equals(group.Id, value, StringComparison.Ordinal));
        if (byId is not null)
        {
            return byId;
        }

        var matches = document.Groups
            .Where(group => string.Equals(
                group.Name,
                value,
                StringComparison.CurrentCultureIgnoreCase))
            .ToList();
        return matches.Count switch
        {
            1 => matches[0],
            > 1 => throw new SavedLibraryException(
                $"Group name is ambiguous: '{value}'. Use the group id."),
            _ => throw new SavedLibraryNotFoundException(
                $"Saved group not found: {value}."),
        };
    }

    public static List<SavedProduct> GroupProducts(
        SavedLibraryDocument document,
        SavedGroup group)
    {
        var ids = group.Items
            .Select(item => item.ProductId)
            .ToHashSet(StringComparer.Ordinal);
        return document.Products
            .Where(product => ids.Contains(product.Id))
            .ToList();
    }

    public static SavedProduct FindProduct(
        SavedLibraryDocument document,
        MonitorTarget target,
        bool required = true)
    {
        var id = $"{target.Domain}:{target.Asin}";
        var product = document.Products.FirstOrDefault(item =>
            string.Equals(item.Id, id, StringComparison.Ordinal));
        if (product is null && required)
        {
            throw new SavedLibraryNotFoundException(
                $"Product not saved: {id}. Add it with 'amz-cli library add'.");
        }
        return product!;
    }
}

internal static class LibraryOutput
{
    public static SavedLibraryDocument FilteredDocument(
        SavedLibraryDocument document,
        IReadOnlyList<SavedProduct> products,
        IReadOnlyList<SavedGroup> groups) => new()
    {
        Schema = document.Schema,
        Revision = document.Revision,
        UpdatedAt = document.UpdatedAt,
        Products = products.ToList(),
        Groups = groups.ToList(),
    };

    public static void Products(
        SavedLibraryDocument document,
        IReadOnlyList<SavedProduct> products,
        OutputMode mode,
        int width)
    {
        if (mode == OutputMode.Json)
        {
            WriteJson(document);
            return;
        }

        if (mode == OutputMode.Tsv)
        {
            Console.WriteLine(
                "id\tasin\tdomain\ttitle\tprice\tcurrency\tgroups\tsaved_at" +
                "\tlast_checked_at");
            foreach (var product in products)
            {
                Console.WriteLine(string.Join('\t',
                    Escape(product.Id),
                    Escape(product.Asin),
                    Escape(product.Domain),
                    Escape(product.Snapshot.Title),
                    Escape(Price(product)),
                    Escape(product.Currency),
                    Escape(GroupNames(document, product)),
                    Escape(product.SavedAt.ToLocalTime()
                        .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
                    Escape(product.LastCheckedAt?.ToLocalTime()
                        .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                        ?? string.Empty)));
            }
            return;
        }

        if (products.Count == 0)
        {
            CliConsole.Stdout(width).MarkupLine("[grey]No saved products.[/]");
            return;
        }

        var table = NewTable();
        table.AddColumn(new TableColumn(new Text("ID", new Style(Color.Yellow)))
        {
            Width = 18,
            NoWrap = true,
        });
        table.AddColumn(new TableColumn(new Text("Title")) { Width = 60 });
        table.AddColumn(new TableColumn(new Text("Price", new Style(Color.Green)))
        {
            Width = 14,
            NoWrap = true,
        });
        table.AddColumn(new TableColumn(new Text("Groups")) { Width = 30 });
        table.AddColumn(new TableColumn(new Text("Saved", new Style(Color.Grey)))
        {
            Width = 12,
            NoWrap = true,
        });
        table.AddColumn(new TableColumn(new Text("Checked", new Style(Color.Grey)))
        {
            Width = 12,
            NoWrap = true,
        });
        foreach (var product in products)
        {
            table.AddRow(
                new Text(product.Id, new Style(Color.Yellow)),
                new Text(TextCleaner.Clean(product.Snapshot.Title, 58))
                    .Overflow(Overflow.Ellipsis),
                new Text(Price(product), new Style(Color.Green)),
                new Text(GroupNames(document, product)),
                new Text(product.SavedAt.ToLocalTime()
                    .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    new Style(Color.Grey)),
                new Text(
                    product.LastCheckedAt?.ToLocalTime()
                        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        ?? "-",
                    new Style(Color.Grey)));
        }
        CliConsole.Stdout(width).Write(table);
    }

    public static void Groups(
        SavedLibraryDocument document,
        IReadOnlyList<SavedGroup> groups,
        OutputMode mode,
        int width)
    {
        if (mode == OutputMode.Json)
        {
            WriteJson(document);
            return;
        }

        if (mode == OutputMode.Tsv)
        {
            Console.WriteLine("id\tname\taccent\tproducts\tdescription");
            foreach (var group in groups)
            {
                Console.WriteLine(string.Join('\t',
                    Escape(group.Id),
                    Escape(group.Name),
                    Escape(group.Accent),
                    group.Items.Count.ToString(CultureInfo.InvariantCulture),
                    Escape(group.Description ?? string.Empty)));
            }
            return;
        }

        if (groups.Count == 0)
        {
            CliConsole.Stdout(width).MarkupLine("[grey]No saved groups.[/]");
            return;
        }

        var table = NewTable();
        table.AddColumn(new TableColumn(new Text("ID", new Style(Color.Yellow)))
        {
            Width = 18,
            NoWrap = true,
        });
        table.AddColumn(new TableColumn(new Text("Name")) { Width = 48 });
        table.AddColumn(new TableColumn(new Text("Color")) { Width = 10 });
        table.AddColumn(new TableColumn(new Text("Description")) { Width = 48 });
        table.AddColumn(new TableColumn(new Text("Products", new Style(Color.Green)))
        {
            Width = 10,
            NoWrap = true,
        });
        foreach (var group in groups)
        {
            table.AddRow(
                new Text(group.Id, new Style(Color.Yellow)),
                new Text(TextCleaner.Clean(group.Name, 46))
                    .Overflow(Overflow.Ellipsis),
                new Text(group.Accent),
                new Text(TextCleaner.Clean(group.Description ?? string.Empty, 46))
                    .Overflow(Overflow.Ellipsis),
                new Text(group.Items.Count.ToString(CultureInfo.InvariantCulture),
                    new Style(Color.Green)));
        }
        CliConsole.Stdout(width).Write(table);
    }

    public static void Group(
        SavedLibraryDocument document,
        SavedGroup group,
        OutputMode mode,
        int width)
    {
        if (mode == OutputMode.Json)
        {
            WriteJson(document);
            return;
        }

        var products = group.Items
            .Select(item => new
            {
                Item = item,
                Product = document.Products.FirstOrDefault(product =>
                    string.Equals(
                        product.Id,
                        item.ProductId,
                        StringComparison.Ordinal)),
            })
            .Where(entry => entry.Product is not null)
            .ToList();

        if (mode == OutputMode.Tsv)
        {
            Console.WriteLine("group_id\tproduct_id\tquantity\ttitle\tprice");
            foreach (var entry in products)
            {
                Console.WriteLine(string.Join('\t',
                    Escape(group.Id),
                    Escape(entry.Item.ProductId),
                    entry.Item.Quantity.ToString(CultureInfo.InvariantCulture),
                    Escape(entry.Product!.Snapshot.Title),
                    Escape(Price(entry.Product))));
            }
            return;
        }

        CliConsole.Stdout(width).MarkupLine(
            $"[bold]{Markup.Escape(group.Name)}[/] " +
            $"[grey]({Markup.Escape(group.Id)}, {products.Count} products)[/]");
        if (!string.IsNullOrWhiteSpace(group.Description))
        {
            CliConsole.Stdout(width).MarkupLine(
                $"[grey]{Markup.Escape(group.Description)}[/]");
        }
        if (products.Count == 0)
        {
            CliConsole.Stdout(width).MarkupLine("[grey]This group is empty.[/]");
            return;
        }

        var table = NewTable();
        table.AddColumn(new TableColumn(new Text("Qty", new Style(Color.Grey)))
        {
            Width = 4,
            NoWrap = true,
        });
        table.AddColumn(new TableColumn(new Text("Product", new Style(Color.Yellow)))
        {
            Width = 18,
            NoWrap = true,
        });
        table.AddColumn(new TableColumn(new Text("Title")) { Width = 58 });
        table.AddColumn(new TableColumn(new Text("Price", new Style(Color.Green)))
        {
            Width = 14,
            NoWrap = true,
        });
        foreach (var entry in products)
        {
            table.AddRow(
                new Text(entry.Item.Quantity.ToString(CultureInfo.InvariantCulture)),
                new Text(entry.Item.ProductId, new Style(Color.Yellow)),
                new Text(TextCleaner.Clean(entry.Product!.Snapshot.Title, 56))
                    .Overflow(Overflow.Ellipsis),
                new Text(Price(entry.Product), new Style(Color.Green)));
        }
        CliConsole.Stdout(width).Write(table);
    }

    private static Table NewTable() => new()
    {
        Border = TableBorder.HeavyHead,
        BorderStyle = new Style(Color.Cyan),
    };

    private static void WriteJson(SavedLibraryDocument document)
    {
        Console.WriteLine(JsonSerializer.Serialize(
            document,
            SavedLibraryJson.Options));
    }

    private static string Price(SavedProduct product)
    {
        if (!string.IsNullOrWhiteSpace(product.Snapshot.Price))
        {
            return product.Snapshot.Price;
        }
        if (product.PriceLast is { } amount)
        {
            return $"{amount.ToString("0.##", CultureInfo.InvariantCulture)} " +
                product.Currency;
        }
        return "-";
    }

    private static string GroupNames(
        SavedLibraryDocument document,
        SavedProduct product)
    {
        var names = document.Groups
            .Where(group => group.Items.Any(item =>
                string.Equals(
                    item.ProductId,
                    product.Id,
                    StringComparison.Ordinal)))
            .Select(group => group.Name)
            .ToList();
        return names.Count == 0 ? "-" : string.Join(", ", names);
    }

    private static string Escape(string value) =>
        value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
