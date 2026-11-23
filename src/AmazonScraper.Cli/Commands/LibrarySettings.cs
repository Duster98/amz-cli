using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Commands;

internal static class LibraryGroupColorHelp
{
    public const string Palette =
        "accent, blue, gold, rust, green, teal, violet, rose, slate, orange, " +
        "plum, sky, or #RRGGBB";
}

public class LibraryOutputSettings : CommandSettings
{
    [CommandOption("--domain <DOMAIN>")]
    [Description("Amazon marketplace: it (default), de, fr, uk, com/us, jp, es")]
    public string? Domain { get; set; } =
        Environment.GetEnvironmentVariable("AMZ_DOMAIN");

    [CommandOption("--width <COLUMNS>")]
    [Description("Console width for tables (default 200)")]
    [DefaultValue(200)]
    public int Width { get; set; } = 200;

    [CommandOption("--json")]
    [Description("JSON output")]
    public bool Json { get; set; }

    [CommandOption("--tk-opt")]
    [Description("Tab-separated output")]
    public bool TkOpt { get; set; }

    public override ValidationResult Validate() =>
        Width < 40
            ? ValidationResult.Error("--width must be at least 40.")
            : ValidationResult.Success();
}

public sealed class LibraryAddSettings : LibraryOutputSettings
{
    [CommandOption("--timeout <SECONDS>")]
    [Description("HTTP timeout in seconds (default: 30 or the saved setting)")]
    public double? Timeout { get; set; }

    [CommandArgument(0, "<PRODUCTS>")]
    [Description("ASIN, domain:ASIN, or Amazon URL")]
    public string[] Products { get; set; } = [];

    [CommandOption("--group <GROUP>")]
    [Description("Add the products to an existing group (id or unique name)")]
    public string? Group { get; set; }

    [CommandOption("--quantity <N>")]
    [Description("Quantity for the group items (default 1)")]
    [DefaultValue(1)]
    public int Quantity { get; set; } = 1;

    public override ValidationResult Validate()
    {
        var baseResult = base.Validate();
        if (!baseResult.Successful)
        {
            return baseResult;
        }
        if (Products.Length == 0)
        {
            return ValidationResult.Error("Specify at least one product.");
        }
        if (Quantity is < 1 or > 99)
        {
            return ValidationResult.Error("--quantity must be between 1 and 99.");
        }
        return Timeout is <= 0
            ? ValidationResult.Error("--timeout must be greater than zero.")
            : ValidationResult.Success();
    }
}

public sealed class LibraryListSettings : LibraryOutputSettings
{
    [CommandOption("--group <GROUP>")]
    [Description("Only products in this group (id or unique name)")]
    public string? Group { get; set; }
}

public sealed class LibraryShowSettings : LibraryOutputSettings
{
    [CommandArgument(0, "<PRODUCT>")]
    [Description("ASIN, domain:ASIN, or Amazon URL")]
    public string Product { get; set; } = string.Empty;
}

public sealed class LibraryRemoveSettings : LibraryOutputSettings
{
    [CommandArgument(0, "<PRODUCTS>")]
    [Description("ASINs, domain:ASINs, or Amazon URLs")]
    public string[] Products { get; set; } = [];

    public override ValidationResult Validate()
    {
        var baseResult = base.Validate();
        if (!baseResult.Successful)
        {
            return baseResult;
        }
        return Products.Length == 0
            ? ValidationResult.Error("Specify at least one product.")
            : ValidationResult.Success();
    }
}

public sealed class LibraryGroupCreateSettings : LibraryOutputSettings
{
    [CommandArgument(0, "<NAME>")]
    [Description("Group name")]
    public string Name { get; set; } = string.Empty;

    [CommandOption("--accent <ACCENT>")]
    [Description(LibraryGroupColorHelp.Palette + " (default: accent)")]
    [DefaultValue("accent")]
    public string Accent { get; set; } = "accent";

    [CommandOption("--description <TEXT>")]
    [Description("Optional group description (max 500 characters)")]
    public string? Description { get; set; }

    public override ValidationResult Validate()
    {
        var baseResult = base.Validate();
        if (!baseResult.Successful)
        {
            return baseResult;
        }
        if (string.IsNullOrWhiteSpace(Name))
        {
            return ValidationResult.Error("A group name is required.");
        }
        if (!AmazonScraper.Cli.Library.SavedLibraryNormalizer.IsValidAccent(Accent))
        {
            return ValidationResult.Error(
                "Invalid color: use a preset name or #RRGGBB.");
        }
        return (Description?.Length ?? 0) > AmazonScraper.Cli.Library.SavedLibraryNormalizer.MaxGroupDescriptionLength
            ? ValidationResult.Error(
                $"Description cannot exceed {AmazonScraper.Cli.Library.SavedLibraryNormalizer.MaxGroupDescriptionLength} characters.")
            : ValidationResult.Success();
    }
}

public sealed class LibraryGroupListSettings : LibraryOutputSettings
{
}

public sealed class LibraryGroupShowSettings : LibraryOutputSettings
{
    [CommandArgument(0, "<GROUP>")]
    [Description("Group id or unique name")]
    public string Group { get; set; } = string.Empty;
}

public sealed class LibraryGroupAddSettings : LibraryOutputSettings
{
    [CommandArgument(0, "<GROUP>")]
    [Description("Group id or unique name")]
    public string Group { get; set; } = string.Empty;

    [CommandArgument(1, "<PRODUCTS>")]
    [Description("Saved products to add (ASIN, domain:ASIN, or URL)")]
    public string[] Products { get; set; } = [];

    [CommandOption("--quantity <N>")]
    [Description("Quantity per product (default 1)")]
    [DefaultValue(1)]
    public int Quantity { get; set; } = 1;

    public override ValidationResult Validate()
    {
        var baseResult = base.Validate();
        if (!baseResult.Successful)
        {
            return baseResult;
        }
        if (Products.Length == 0)
        {
            return ValidationResult.Error("Specify at least one product.");
        }
        return Quantity is < 1 or > 99
            ? ValidationResult.Error("--quantity must be between 1 and 99.")
            : ValidationResult.Success();
    }
}

public sealed class LibraryGroupRemoveSettings : LibraryOutputSettings
{
    [CommandArgument(0, "<GROUP>")]
    [Description("Group id or unique name")]
    public string Group { get; set; } = string.Empty;

    [CommandArgument(1, "<PRODUCTS>")]
    [Description("Saved products to remove")]
    public string[] Products { get; set; } = [];

    public override ValidationResult Validate()
    {
        var baseResult = base.Validate();
        if (!baseResult.Successful)
        {
            return baseResult;
        }
        return Products.Length == 0
            ? ValidationResult.Error("Specify at least one product.")
            : ValidationResult.Success();
    }
}

public sealed class LibraryGroupRenameSettings : LibraryOutputSettings
{
    [CommandArgument(0, "<GROUP>")]
    [Description("Group id or unique name")]
    public string Group { get; set; } = string.Empty;

    [CommandArgument(1, "<NAME>")]
    [Description("New group name")]
    public string Name { get; set; } = string.Empty;

    [CommandOption("--accent <ACCENT>")]
    [Description(LibraryGroupColorHelp.Palette)]
    public string? Accent { get; set; }

    [CommandOption("--description <TEXT>")]
    [Description("Optional group description (max 500 characters)")]
    public string? Description { get; set; }

    public override ValidationResult Validate()
    {
        var baseResult = base.Validate();
        if (!baseResult.Successful)
        {
            return baseResult;
        }
        if (string.IsNullOrWhiteSpace(Name))
        {
            return ValidationResult.Error("A group name is required.");
        }
        if (Accent is not null
            && !AmazonScraper.Cli.Library.SavedLibraryNormalizer.IsValidAccent(Accent))
        {
            return ValidationResult.Error(
                "Invalid color: use a preset name or #RRGGBB.");
        }
        return (Description?.Length ?? 0) > AmazonScraper.Cli.Library.SavedLibraryNormalizer.MaxGroupDescriptionLength
            ? ValidationResult.Error(
                $"Description cannot exceed {AmazonScraper.Cli.Library.SavedLibraryNormalizer.MaxGroupDescriptionLength} characters.")
            : ValidationResult.Success();
    }
}

public sealed class LibraryGroupEditSettings : LibraryOutputSettings
{
    [CommandArgument(0, "<GROUP>")]
    [Description("Group id or unique name")]
    public string Group { get; set; } = string.Empty;

    [CommandOption("--name <NAME>")]
    [Description("New group name")]
    public string? Name { get; set; }

    [CommandOption("--accent <ACCENT>")]
    [Description(LibraryGroupColorHelp.Palette)]
    public string? Accent { get; set; }

    [CommandOption("--description <TEXT>")]
    [Description("New group description (max 500 characters)")]
    public string? Description { get; set; }

    [CommandOption("--clear-description")]
    [Description("Remove the group description")]
    public bool ClearDescription { get; set; }

    public override ValidationResult Validate()
    {
        var baseResult = base.Validate();
        if (!baseResult.Successful)
        {
            return baseResult;
        }
        if (string.IsNullOrWhiteSpace(Group))
        {
            return ValidationResult.Error("A group is required.");
        }
        if (Name is not null && string.IsNullOrWhiteSpace(Name))
        {
            return ValidationResult.Error("A group name cannot be empty.");
        }
        if (Accent is not null
            && !AmazonScraper.Cli.Library.SavedLibraryNormalizer.IsValidAccent(Accent))
        {
            return ValidationResult.Error(
                "Invalid color: use a preset name or #RRGGBB.");
        }
        if (Description is not null && ClearDescription)
        {
            return ValidationResult.Error(
                "Use --description or --clear-description, not both.");
        }
        if (Description?.Length > AmazonScraper.Cli.Library.SavedLibraryNormalizer.MaxGroupDescriptionLength)
        {
            return ValidationResult.Error(
                $"Description cannot exceed {AmazonScraper.Cli.Library.SavedLibraryNormalizer.MaxGroupDescriptionLength} characters.");
        }
        if (Name is null && Accent is null && Description is null && !ClearDescription)
        {
            return ValidationResult.Error(
                "Specify --name, --accent, --description, or --clear-description.");
        }
        return ValidationResult.Success();
    }
}

public sealed class LibraryGroupDeleteSettings : LibraryOutputSettings
{
    [CommandArgument(0, "<GROUP>")]
    [Description("Group id or unique name")]
    public string Group { get; set; } = string.Empty;
}
public sealed class LibraryImportMonitorSettings : LibraryOutputSettings
{
    [CommandArgument(0, "<CONFIG>")]
    [Description("Legacy monitor JSON configuration to import")]
    public string Config { get; set; } = string.Empty;

    [CommandOption("--dry-run")]
    [Description("Validate and show the import summary without writing")]
    public bool DryRun { get; set; }

    public override ValidationResult Validate()
    {
        var baseResult = base.Validate();
        if (!baseResult.Successful)
        {
            return baseResult;
        }
        return string.IsNullOrWhiteSpace(Config)
            ? ValidationResult.Error("A monitor configuration is required.")
            : ValidationResult.Success();
    }
}
