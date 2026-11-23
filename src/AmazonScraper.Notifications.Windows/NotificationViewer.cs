using System.Globalization;
using System.Text.Json;
using Spectre.Console;

namespace AmazonScraper.Notifications.Windows;

internal static class NotificationViewer
{
    public static int Show(string? payloadPath)
    {
        var payload = Load(payloadPath);
        try
        {
            if (!Console.IsOutputRedirected)
            {
                AnsiConsole.Clear();
            }
            AnsiConsole.Profile.Width = GetWidth();
        }
        catch (Exception)
        {
            // Non-interactive host; keep the defaults.
        }

        try
        {
            Console.Title = "AMZ CLI Monitor";
        }
        catch (Exception)
        {
            // Console title is not available in every host.
        }

        Render(payload);
        WaitForClose();
        return 0;
    }

    private static NotificationPayload? Load(string? payloadPath)
    {
        if (string.IsNullOrWhiteSpace(payloadPath) || !File.Exists(payloadPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<NotificationPayload>(
                File.ReadAllText(payloadPath),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void Render(NotificationPayload? payload)
    {
        AnsiConsole.Write(
            new Rule("[bold teal]AMZ CLI Monitor[/]")
                .LeftJustified()
                .RuleStyle("grey"));

        if (payload is null)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Details unavailable[/]");
            AnsiConsole.MarkupLine(
                "[grey]The notification file is no longer available.[/]");
            return;
        }

        AnsiConsole.WriteLine();
        switch (payload.Kind)
        {
            case "test":
                RenderTest(payload);
                break;
            case "group_alert":
                RenderGroup(payload);
                break;
            case "alert":
                RenderAlert(payload);
                break;
            case "errors":
                RenderErrors(payload);
                break;
            case "update":
                RenderUpdate(payload);
                break;
            default:
                RenderEvent(payload);
                break;
        }
    }

    private static void RenderTest(NotificationPayload payload)
    {
        AnsiConsole.Write(
            new Panel(
                    new Markup(
                        $"[bold]{Escape(payload.Title)}[/]\n\n" +
                        $"{Escape(payload.Body)}\n\n" +
                        "[grey]The notification was opened from the Windows " +
                        "Action Center.[/]"))
                .Border(BoxBorder.Square)
                .BorderColor(Color.Teal)
                .Padding(1, 1));
    }

    private static void RenderAlert(NotificationPayload payload)
    {
        AnsiConsole.MarkupLine($"[bold]{Escape(payload.Title)}[/]");
        AnsiConsole.WriteLine();

        var item = payload.Items.FirstOrDefault();
        if (item is null)
        {
            AnsiConsole.MarkupLine("[grey]No details available.[/]");
            return;
        }

        AnsiConsole.MarkupLine($"[bold]{Escape(Short(item.Title ?? item.Asin, 90))}[/]");
        AnsiConsole.MarkupLine(
            $"[grey]{Escape(item.Domain)} / {Escape(item.Asin)}[/]");
        AnsiConsole.WriteLine();

        var priceLine = $"[bold gold1]{Escape(item.Price)}[/]";
        if (!string.IsNullOrWhiteSpace(item.OriginalPrice))
        {
            priceLine += $"  [grey]era {Escape(item.OriginalPrice)}[/]";
        }
        if (!string.IsNullOrWhiteSpace(item.Discount))
        {
            priceLine += $"  [green]{Escape(item.Discount)}[/]";
        }
        AnsiConsole.MarkupLine(priceLine);

        if (item.Threshold is not null)
        {
            AnsiConsole.MarkupLine(
                $"[grey]Soglia[/] {FormatAmount(item.Threshold, item.Currency)}");
        }
        if (!string.IsNullOrWhiteSpace(item.Deal))
        {
            AnsiConsole.MarkupLine(
                $"[grey]Badge[/] [gold1]{Escape(item.Deal)}[/]");
        }
        if (!string.IsNullOrWhiteSpace(item.DealEndsText))
        {
            AnsiConsole.MarkupLine(
                $"[grey]Scade[/] [gold1]{Escape(item.DealEndsText)}[/]");
        }
        else if (!string.IsNullOrWhiteSpace(item.DealEndsOn))
        {
            AnsiConsole.MarkupLine(
                $"[grey]Scade[/] [gold1]{Escape(item.DealEndsOn)}[/]");
        }
        if (!string.IsNullOrWhiteSpace(item.Coupon))
        {
            AnsiConsole.MarkupLine(
                $"[grey]Coupon[/] [green]{Escape(item.Coupon)}[/]");
        }
        if (!string.IsNullOrWhiteSpace(item.Error))
        {
            AnsiConsole.MarkupLine($"[red]{Escape(item.Error)}[/]");
        }
    }

    private static void RenderGroup(NotificationPayload payload)
    {
        AnsiConsole.MarkupLine($"[bold]{Escape(payload.Title)}[/]");
        AnsiConsole.MarkupLine(
            $"[bold]{Escape(payload.GroupName ?? payload.GroupId ?? "Gruppo")}[/]");
        AnsiConsole.MarkupLine(
            $"[grey]Total[/] [bold gold1]" +
            $"{FormatAmount(payload.GroupTotal, payload.Currency)}[/]  " +
            $"[grey]≤ threshold[/] " +
            $"{FormatAmount(payload.GroupThreshold, payload.Currency)}");
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Simple)
            .BorderColor(Color.Grey)
            .Expand();
        table.AddColumn("Product");
        table.AddColumn(new TableColumn("Qty") { Alignment = Justify.Right });
        table.AddColumn(new TableColumn("Price") { Alignment = Justify.Right });
        table.AddColumn(new TableColumn("Total") { Alignment = Justify.Right });

        foreach (var item in payload.Items)
        {
            table.AddRow(
                new Markup(
                    $"[bold]{Escape(Short(item.Title ?? item.Asin, 65))}[/]\n" +
                    $"[grey]{Escape(item.Domain)} / {Escape(item.Asin)}[/]"),
                new Text((item.Quantity ?? 1).ToString(CultureInfo.InvariantCulture)),
                new Text(item.Price ?? "—"),
                new Text(FormatAmount(item.LineTotal, item.Currency)));
        }

        AnsiConsole.Write(table);
    }

    private static void RenderErrors(NotificationPayload payload)
    {
        AnsiConsole.MarkupLine($"[bold]{Escape(payload.Title)}[/]");
        AnsiConsole.MarkupLine($"[grey]{Escape(payload.Body)}[/]");
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Simple)
            .BorderColor(Color.Grey)
            .Expand();
        table.AddColumn("Dominio");
        table.AddColumn("ASIN");
        table.AddColumn("Error");

        foreach (var item in payload.Items)
        {
            table.AddRow(
                new Text(item.Domain ?? "—"),
                new Text(item.Asin ?? "—"),
                new Markup($"[red]{Escape(Short(item.Error ?? item.Message, 110))}[/]"));
        }

        AnsiConsole.Write(table);
    }

    private static void RenderEvent(NotificationPayload payload)
    {
        var color = payload.Kind is "block" or "domain_disabled" or "fatal"
            ? Color.Red
            : Color.Teal;
        AnsiConsole.Write(
            new Panel(
                    new Markup(
                        $"[bold]{Escape(payload.Title)}[/]\n\n" +
                        $"{Escape(payload.Body)}"))
                .Border(BoxBorder.Square)
                .BorderColor(color)
                .Padding(1, 1));
    }

    private static void RenderUpdate(NotificationPayload payload)
    {
        var color = payload.UpdateStatus switch
        {
            "rolled_back" or "failed" => Color.Red,
            "deferred_foreground" => Color.Gold1,
            _ => Color.Teal,
        };
        AnsiConsole.Write(
            new Panel(
                    new Markup(
                        $"[bold]{Escape(payload.Title)}[/]\n\n" +
                        $"{Escape(payload.Body)}"))
                .Border(BoxBorder.Square)
                .BorderColor(color)
                .Padding(1, 1));

        var table = new Table()
            .Border(TableBorder.Simple)
            .BorderColor(Color.Grey)
            .Expand();
        table.AddColumn("Item");
        table.AddColumn("Value");
        if (!string.IsNullOrWhiteSpace(payload.FromVersion)
            || !string.IsNullOrWhiteSpace(payload.ToVersion))
        {
            table.AddRow(
                "Version",
                $"{Escape(payload.FromVersion ?? "?")} → " +
                $"{Escape(payload.ToVersion ?? "?")}");
        }
        if (!string.IsNullOrWhiteSpace(payload.UpdateStatus))
        {
            table.AddRow("Result", Escape(payload.UpdateStatus));
        }
        if (payload.MonitorsRestarted is { } restarted)
        {
            table.AddRow(
                "Monitor",
                restarted ? "riavviati" : "non riavviati");
        }
        if (!string.IsNullOrWhiteSpace(payload.Trigger))
        {
            table.AddRow("Origine", Escape(payload.Trigger));
        }
        if (!string.IsNullOrWhiteSpace(payload.BackupPath))
        {
            table.AddRow("Backup", Escape(payload.BackupPath));
        }
        if (!string.IsNullOrWhiteSpace(payload.LogPath))
        {
            table.AddRow("Log", Escape(payload.LogPath));
        }
        if (!string.IsNullOrWhiteSpace(payload.Error))
        {
            table.AddRow("Error", $"[red]{Escape(payload.Error)}[/]");
        }
        AnsiConsole.Write(table);

        if (!string.IsNullOrWhiteSpace(payload.Notes))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(
                new Panel(new Markup(Escape(payload.Notes)))
                {
                    Header = new PanelHeader("Note di rilascio"),
                    Border = BoxBorder.Rounded,
                    BorderStyle = new Style(Color.Grey),
                    Padding = new Padding(1, 0, 1, 0),
                });
        }
    }

    private static void WaitForClose()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Press any key to close.[/]");
        try
        {
            if (!Console.IsInputRedirected)
            {
                Console.ReadKey(intercept: true);
            }
            else
            {
                Console.ReadLine();
            }
        }
        catch (Exception)
        {
            // The window may already be closing.
        }
    }

    private static int GetWidth()
    {
        try
        {
            return Math.Clamp(Console.WindowWidth - 2, 60, 110);
        }
        catch (Exception)
        {
            return 100;
        }
    }

    private static string FormatAmount(decimal? amount, string? currency)
    {
        if (amount is null)
        {
            return "—";
        }

        var code = currency?.ToUpperInvariant();
        var decimals = code == "JPY" ? 0 : 2;
        var number = amount.Value.ToString(
            $"N{decimals}",
            CultureInfo.InvariantCulture);
        if (code == "EUR")
        {
            number = number
                .Replace(",", "\u0001", StringComparison.Ordinal)
                .Replace(".", ",", StringComparison.Ordinal)
                .Replace("\u0001", ".", StringComparison.Ordinal);
        }

        return code switch
        {
            "EUR" => $"{number} €",
            "GBP" => $"£{number}",
            "USD" => $"${number}",
            "JPY" => $"¥{number}",
            _ => string.IsNullOrWhiteSpace(currency)
                ? number
                : $"{number} {currency}",
        };
    }

    private static string Short(string? text, int maxLength)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..(maxLength - 1)] + "…";
    }

    private static string Escape(string? text) =>
        Markup.Escape(text ?? string.Empty);
}
