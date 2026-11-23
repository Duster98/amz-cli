using AmazonScraper.Cli.Commands;

namespace AmazonScraper.Cli.Tests;

public class MonitorSettingsTests
{
    [Fact]
    public void Timer_accepts_ten_seconds()
    {
        var settings = new MonitorSettings
        {
            Asins = ["B000000001"],
            Timer = 10,
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Ten_minute_timer_accepts_rise_and_drop_thresholds()
    {
        var settings = new MonitorSettings
        {
            LibraryProduct = "B000000001",
            Timer = 600,
            DropPercent = 5,
            RisePercent = 10,
            Baseline = "first_seen",
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Rise_percent_above_one_hundred_is_rejected()
    {
        var settings = new MonitorSettings
        {
            LibraryProduct = "B000000001",
            RisePercent = 101,
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Timer_below_ten_seconds_is_rejected()
    {
        var settings = new MonitorSettings
        {
            Asins = ["B000000001"],
            Timer = 9,
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Request_delay_below_six_seconds_is_rejected_for_network_runs()
    {
        var settings = new MonitorSettings
        {
            Asins = ["B000000001"],
            RequestDelay = 5,
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Html_run_can_use_zero_request_delay()
    {
        var settings = new MonitorSettings
        {
            Asins = ["B000000001"],
            HtmlFile = "/tmp/product.html",
            RequestDelay = 0,
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Library_group_accepts_currency_and_fx_options()
    {
        var settings = new MonitorSettings
        {
            LibraryGroup = "g_test",
            TotalMax = 1200,
            Currency = "EUR",
            Fx = ["GBP=1.17"],
            MaxPrice = 500,
            DropPercent = 10,
            Timer = 3600,
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Library_targets_are_mutually_exclusive()
    {
        var settings = new MonitorSettings
        {
            LibraryGroup = "g_test",
            LibraryProduct = "B000000001",
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Library_monitoring_rejects_adaptive_scheduling()
    {
        var settings = new MonitorSettings
        {
            LibraryGroup = "g_test",
            Schedule = "adaptive",
            MaxRequests = 10,
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Binding_cannot_be_combined_with_threshold_edits()
    {
        var settings = new MonitorSettings
        {
            Binding = "group:g_test",
            TotalMax = 100,
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Binding_list_is_a_valid_control_command()
    {
        var settings = new MonitorSettings
        {
            ListBindings = true,
            Json = true,
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Single_layout_with_csv_is_rejected()
    {
        var settings = new MonitorSettings
        {
            Asins = ["B000000001"],
            Layout = "single",
            SaveFormat = "csv",
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Adaptive_schedule_options_are_valid()
    {
        var settings = new MonitorSettings
        {
            Asins = ["B000000001"],
            Schedule = "adaptive",
            Window = 86_400,
            MaxRequests = 500,
            MinGap = 600,
            MaxGap = 1200,
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Adaptive_schedule_rejects_timer_and_jitter()
    {
        var timer = new MonitorSettings
        {
            Asins = ["B000000001"],
            Schedule = "adaptive",
            MaxRequests = 10,
            Timer = 60,
        };
        Assert.False(timer.Validate().Successful);

        var jitter = new MonitorSettings
        {
            Asins = ["B000000001"],
            Schedule = "adaptive",
            MaxRequests = 10,
            Jitter = 5,
        };
        Assert.False(jitter.Validate().Successful);
    }

    [Fact]
    public void Adaptive_schedule_rejects_invalid_values()
    {
        Assert.False(new MonitorSettings
        {
            Asins = ["B000000001"],
            Schedule = "quota",
            MaxRequests = 10,
        }.Validate().Successful);

        Assert.False(new MonitorSettings
        {
            Asins = ["B000000001"],
            Schedule = "adaptive",
            MaxRequests = 0,
        }.Validate().Successful);

        Assert.False(new MonitorSettings
        {
            Asins = ["B000000001"],
            Schedule = "adaptive",
            MaxRequests = 10,
            MinGap = 120,
            MaxGap = 60,
        }.Validate().Successful);
    }

    [Fact]
    public void Once_and_cycles_are_mutually_exclusive()
    {
        var settings = new MonitorSettings
        {
            Asins = ["B000000001"],
            Once = true,
            Cycles = 2,
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Html_requires_exactly_one_asin_and_no_config()
    {
        var settings = new MonitorSettings
        {
            Asins = ["B000000001"],
            HtmlFile = "/tmp/product.html",
            Config = "/tmp/monitor.json",
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Save_file_cannot_contain_a_directory()
    {
        var settings = new MonitorSettings
        {
            Asins = ["B000000001"],
            SaveFile = "../escape",
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Cli_thresholds_require_cli_targets()
    {
        var settings = new MonitorSettings
        {
            Config = "/tmp/monitor.json",
            MaxPrice = 100,
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Config_only_is_a_valid_source()
    {
        var settings = new MonitorSettings
        {
            Config = "/tmp/monitor.json",
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Status_does_not_require_targets()
    {
        var settings = new MonitorSettings
        {
            Status = true,
            SavePath = "/tmp/monitor",
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Status_rejects_asins()
    {
        var settings = new MonitorSettings
        {
            Status = true,
            Asins = ["B000000001"],
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Detach_rejects_once()
    {
        var settings = new MonitorSettings
        {
            Detach = true,
            Once = true,
            Asins = ["B000000001"],
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Detach_requires_targets()
    {
        var settings = new MonitorSettings
        {
            Detach = true,
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Status_and_stop_are_mutually_exclusive()
    {
        var settings = new MonitorSettings
        {
            Status = true,
            Stop = true,
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Notify_test_does_not_require_targets()
    {
        var settings = new MonitorSettings
        {
            NotifyTest = true,
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Notify_on_is_validated_later_against_config_and_notify()
    {
        var settings = new MonitorSettings
        {
            Asins = ["B000000001"],
            NotifyOn = "all",
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Notify_on_accepts_all()
    {
        var settings = new MonitorSettings
        {
            Asins = ["B000000001"],
            Notify = true,
            NotifyOn = "all",
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Invalid_notify_event_is_rejected()
    {
        var settings = new MonitorSettings
        {
            Asins = ["B000000001"],
            Notify = true,
            NotifyOn = "price",
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Notify_test_rejects_targets()
    {
        var settings = new MonitorSettings
        {
            NotifyTest = true,
            Asins = ["B000000001"],
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Notify_cooldown_must_be_at_least_one_minute()
    {
        var settings = new MonitorSettings
        {
            Asins = ["B000000001"],
            Notify = true,
            NotifyCooldown = 59,
        };

        Assert.False(settings.Validate().Successful);
    }
}
