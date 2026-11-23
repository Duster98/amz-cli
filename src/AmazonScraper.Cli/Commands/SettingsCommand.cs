using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AmazonScraper.Cli.Diagnostics;
using AmazonScraper.Cli.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Commands;

public sealed class SettingsShowSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("JSON output")]
    public bool Json { get; set; }
}

public sealed class SettingsSetSettings : CommandSettings
{
    [CommandOption("--transport <TRANSPORT>")]
    [Description("HTTP transport: auto, curl, or http")]
    public string? Transport { get; set; }

    [CommandOption("--browser <PROFILE>")]
    [Description("curl-impersonate profile, or auto")]
    public string? Browser { get; set; }

    [CommandOption("--user-agent <UA>")]
    [Description("Explicit User-Agent; use auto or --no-user-agent to clear it")]
    public string? UserAgent { get; set; }

    [CommandOption("--no-user-agent")]
    [Description("Clear the saved User-Agent")]
    public bool NoUserAgent { get; set; }

    [CommandOption("--browser-headers")]
    [Description("Force the full browser headers on every marketplace")]
    public bool BrowserHeaders { get; set; }

    [CommandOption("--no-browser-headers")]
    [Description("Do not force extra browser headers")]
    public bool NoBrowserHeaders { get; set; }

    [CommandOption("--timeout <SECONDS>")]
    [Description("HTTP timeout in seconds (5-120)")]
    public double? Timeout { get; set; }

    [CommandOption("--fallback")]
    [Description("Retry searches with curl when HTTP is blocked")]
    public bool Fallback { get; set; }

    [CommandOption("--no-fallback")]
    [Description("Do not retry with curl when HTTP is blocked")]
    public bool NoFallback { get; set; }

    [CommandOption("--json")]
    [Description("JSON output")]
    public bool Json { get; set; }

    public override ValidationResult Validate()
    {
        if (BrowserHeaders && NoBrowserHeaders)
        {
            return ValidationResult.Error(
                "Use --browser-headers or --no-browser-headers, not both.");
        }
        if (Fallback && NoFallback)
        {
            return ValidationResult.Error(
                "Use --fallback or --no-fallback, not both.");
        }
        if (Transport is not null
            && !HttpSettings.TryNormalizeTransport(Transport, out _))
        {
            return ValidationResult.Error(
                "--transport must be auto, curl, or http.");
        }
        if (Browser is not null
            && !HttpSettings.TryNormalizeBrowser(Browser, out _))
        {
            return ValidationResult.Error(
                $"Invalid browser profile: '{Browser}'. Use auto or one of: " +
                string.Join(", ", HttpSettings.AvailableBrowsers));
        }
        if (Timeout is { } timeout
            && (!double.IsFinite(timeout)
                || timeout is < HttpSettings.MinTimeoutSeconds
                    or > HttpSettings.MaxTimeoutSeconds))
        {
            return ValidationResult.Error(
                $"--timeout must be between " +
                $"{HttpSettings.MinTimeoutSeconds:0} and " +
                $"{HttpSettings.MaxTimeoutSeconds:0} seconds.");
        }
        if (UserAgent is not null)
        {
            if (UserAgent.Any(char.IsControl))
            {
                return ValidationResult.Error(
                    "--user-agent cannot contain control characters.");
            }
            if (UserAgent.Trim().Length > 256)
            {
                return ValidationResult.Error(
                    "--user-agent cannot exceed 256 characters.");
            }
        }

        var hasChange = Transport is not null
            || Browser is not null
            || UserAgent is not null
            || NoUserAgent
            || BrowserHeaders
            || NoBrowserHeaders
            || Timeout is not null
            || Fallback
            || NoFallback;
        return hasChange
            ? ValidationResult.Success()
            : ValidationResult.Error(
                "Specify at least one setting to change.");
    }
}

public sealed class SettingsResetSettings : CommandSettings
{
}

public sealed class SettingsPathSettings : CommandSettings
{
}

public sealed class SettingsShowCommand : Command<SettingsShowSettings>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpSettingsStore _store;
    private readonly TransportCooldownStore _cooldowns;

    public SettingsShowCommand()
        : this(new HttpSettingsStore(), TransportCooldownStore.Default)
    {
    }

    internal SettingsShowCommand(
        HttpSettingsStore store,
        TransportCooldownStore cooldowns)
    {
        _store = store;
        _cooldowns = cooldowns;
    }

    public override int Execute(
        CommandContext context,
        SettingsShowSettings settings,
        CancellationToken cancellationToken)
    {
        var report = HttpDiagnostics.Build(
            _store.Load(),
            cooldowns: _cooldowns);
        if (settings.Json)
        {
            Console.Out.WriteLine(
                JsonSerializer.Serialize(report, JsonOptions));
            return 0;
        }

        Render(report, _store.FilePath);
        return 0;
    }

    private static void Render(
        HttpSettingsDiagnostics report,
        string filePath)
    {
        var saved = new Table()
            .Border(TableBorder.HeavyHead)
            .BorderStyle(new Style(Color.Cyan))
            .Title("Saved HTTP settings");
        saved.AddColumn(new TableColumn(new Text("Setting")));
        saved.AddColumn(new TableColumn(new Text("Value")));
        saved.AddRow("File", filePath);
        saved.AddRow("Transport", report.Settings.Transport);
        saved.AddRow("Browser", report.Settings.Browser);
        saved.AddRow(
            "User-Agent",
            string.IsNullOrWhiteSpace(report.Settings.UserAgent)
                ? "auto"
                : report.Settings.UserAgent);
        saved.AddRow(
            "Browser headers",
            report.Settings.BrowserHeaders ? "forced" : "auto");
        saved.AddRow(
            "Timeout",
            $"{report.Settings.TimeoutSeconds.ToString("0.###", CultureInfo.InvariantCulture)} s");
        saved.AddRow(
            "Fallback to curl",
            report.Settings.FallbackToCurl ? "on" : "off");
        saved.AddRow(
            "Updated",
            report.Settings.UpdatedAt.ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        AnsiConsole.Write(saved);

        var effective = new Table()
            .Border(TableBorder.HeavyHead)
            .BorderStyle(new Style(Color.Cyan))
            .Title("Effective configuration");
        effective.AddColumn(new TableColumn(new Text("Marketplace")));
        effective.AddColumn(new TableColumn(new Text("Transport")));
        effective.AddColumn(new TableColumn(new Text("Profile")));
        effective.AddColumn(new TableColumn(new Text("Headers")));
        effective.AddColumn(new TableColumn(new Text("Timeout")));
        foreach (var item in report.Effective)
        {
            effective.AddRow(
                item.Domain,
                item.Transport,
                item.Browser,
                item.BrowserHeaders ? "on" : "off",
                $"{item.TimeoutSeconds.ToString("0.###", CultureInfo.InvariantCulture)} s");
        }
        AnsiConsole.Write(effective);

        var curl = new Table()
            .Border(TableBorder.HeavyHead)
            .BorderStyle(new Style(Color.Cyan))
            .Title("curl-impersonate");
        curl.AddColumn(new TableColumn(new Text("Item")));
        curl.AddColumn(new TableColumn(new Text("Value")));
        curl.AddRow("Available", report.Curl.Available ? "yes" : "no");
        curl.AddRow("Version", report.Curl.Version);
        curl.AddRow("Path", report.Curl.Runtime);
        curl.AddRow("Mode", report.Curl.Mode);
        curl.AddRow("Transport", report.Curl.Transport);
        if (!string.IsNullOrWhiteSpace(report.Curl.Error))
        {
            curl.AddRow("Error", report.Curl.Error);
        }
        AnsiConsole.Write(curl);

        if (report.Cooldowns.Count > 0)
        {
            var cooldowns = new Table()
                .Border(TableBorder.HeavyHead)
                .BorderStyle(new Style(Color.Cyan))
                .Title("Active HTTP cooldowns");
            cooldowns.AddColumn(new TableColumn(new Text("Marketplace")));
            cooldowns.AddColumn(new TableColumn(new Text("Until")));
            cooldowns.AddColumn(new TableColumn(new Text("Reason")));
            foreach (var item in report.Cooldowns)
            {
                cooldowns.AddRow(
                    item.Domain,
                    item.Until.ToLocalTime()
                        .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    item.Reason);
            }
            AnsiConsole.Write(cooldowns);
        }
    }
}

public sealed class SettingsSetCommand : Command<SettingsSetSettings>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpSettingsStore _store;

    public SettingsSetCommand()
        : this(new HttpSettingsStore())
    {
    }

    internal SettingsSetCommand(HttpSettingsStore store)
    {
        _store = store;
    }

    public override int Execute(
        CommandContext context,
        SettingsSetSettings settings,
        CancellationToken cancellationToken)
    {
        var saved = _store.Load();
        if (settings.Transport is not null
            && HttpSettings.TryNormalizeTransport(
                settings.Transport,
                out var transport))
        {
            saved.Transport = transport;
        }
        if (settings.Browser is not null
            && HttpSettings.TryNormalizeBrowser(
                settings.Browser,
                out var browser))
        {
            saved.Browser = browser;
        }
        if (settings.NoUserAgent
            || (settings.UserAgent is not null
                && (string.IsNullOrWhiteSpace(settings.UserAgent)
                    || settings.UserAgent.Equals(
                        HttpSettings.Auto,
                        StringComparison.OrdinalIgnoreCase))))
        {
            saved.UserAgent = string.Empty;
        }
        else if (settings.UserAgent is not null)
        {
            saved.UserAgent = HttpSettings.NormalizeUserAgent(settings.UserAgent);
        }
        if (settings.BrowserHeaders)
        {
            saved.BrowserHeaders = true;
        }
        if (settings.NoBrowserHeaders)
        {
            saved.BrowserHeaders = false;
        }
        if (settings.Timeout is { } timeout)
        {
            saved.TimeoutSeconds = timeout;
        }
        if (settings.Fallback)
        {
            saved.FallbackToCurl = true;
        }
        if (settings.NoFallback)
        {
            saved.FallbackToCurl = false;
        }

        var result = _store.Save(saved);
        if (settings.Json)
        {
            Console.Out.WriteLine(
                JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            Console.Out.WriteLine($"Saved HTTP settings to {_store.FilePath}");
            Console.Out.WriteLine($"Transport: {result.Transport}");
            Console.Out.WriteLine($"Browser: {result.Browser}");
            Console.Out.WriteLine(
                $"User-Agent: {(string.IsNullOrWhiteSpace(result.UserAgent) ? "auto" : result.UserAgent)}");
            Console.Out.WriteLine(
                $"Browser headers: {(result.BrowserHeaders ? "forced" : "auto")}");
            Console.Out.WriteLine(
                $"Timeout: {result.TimeoutSeconds.ToString("0.###", CultureInfo.InvariantCulture)} s");
            Console.Out.WriteLine(
                $"Fallback to curl: {(result.FallbackToCurl ? "on" : "off")}");
        }
        return 0;
    }
}

public sealed class SettingsResetCommand : Command<SettingsResetSettings>
{
    private readonly HttpSettingsStore _store;

    public SettingsResetCommand()
        : this(new HttpSettingsStore())
    {
    }

    internal SettingsResetCommand(HttpSettingsStore store)
    {
        _store = store;
    }

    public override int Execute(
        CommandContext context,
        SettingsResetSettings settings,
        CancellationToken cancellationToken)
    {
        var result = _store.Reset();
        Console.Out.WriteLine(
            $"HTTP settings reset to auto ({_store.FilePath}).");
        Console.Out.WriteLine(
            $"Timeout: {result.TimeoutSeconds.ToString("0.###", CultureInfo.InvariantCulture)} s");
        return 0;
    }
}

public sealed class SettingsPathCommand : Command<SettingsPathSettings>
{
    private readonly HttpSettingsStore _store;

    public SettingsPathCommand()
        : this(new HttpSettingsStore())
    {
    }

    internal SettingsPathCommand(HttpSettingsStore store)
    {
        _store = store;
    }

    public override int Execute(
        CommandContext context,
        SettingsPathSettings settings,
        CancellationToken cancellationToken)
    {
        Console.Out.WriteLine(_store.FilePath);
        return 0;
    }
}
