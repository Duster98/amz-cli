using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Core.Tests;

public class MonitorPlanBuilderTests
{
    [Fact]
    public void Group_infers_eur_across_italy_and_germany()
    {
        var config = new MonitorConfig
        {
            Domain = "it",
            Groups =
            [
                new MonitorGroupConfig
                {
                    Id = "pc-build",
                    Name = "PC Build",
                    TotalMax = 1200m,
                    Items =
                    [
                        new MonitorGroupItemConfig
                        {
                            Asin = "B000000001",
                            Quantity = 1,
                            MaxPrice = 250m,
                        },
                        new MonitorGroupItemConfig
                        {
                            Asin = "B000000002",
                            Domain = "de",
                            Quantity = 2,
                        },
                    ],
                },
            ],
        };

        var plan = MonitorPlanBuilder.Build(new MonitorPlanBuildOptions
        {
            Config = config,
            DefaultDomain = "it",
        });

        var group = Assert.Single(plan.Groups);
        Assert.Equal("EUR", group.Currency);
        Assert.Equal(["de", "it"], group.Domains);
        Assert.Equal(2, group.Items[1].Quantity);
        Assert.Equal("de", group.Items[1].Target.Domain);
    }

    [Fact]
    public void Adaptive_schedule_uses_per_unit_gap_overrides()
    {
        var config = new MonitorConfig
        {
            Schedule = new MonitorScheduleConfig
            {
                Mode = "adaptive",
                WindowSeconds = 3600,
                MaxRequests = 20,
                MinGapSeconds = 60,
                MaxGapSeconds = 120,
            },
            Items =
            [
                new MonitorItemConfig
                {
                    Asin = "B000000001",
                    MinGapSeconds = 90,
                    MaxGapSeconds = 180,
                },
            ],
            Groups =
            [
                new MonitorGroupConfig
                {
                    Id = "g1",
                    MinGapSeconds = 120,
                    MaxGapSeconds = 240,
                    Items = [new MonitorGroupItemConfig { Asin = "B000000002" }],
                },
            ],
        };

        var plan = MonitorPlanBuilder.Build(new MonitorPlanBuildOptions
        {
            Config = config,
            DefaultDomain = "it",
        });

        var schedule = Assert.IsType<MonitorSchedulePlan>(plan.Schedule);
        var item = Assert.Single(schedule.Units, unit =>
            unit.Kind == MonitorUnitKind.Item);
        Assert.Equal(90, item.MinGapSeconds);
        Assert.Equal(180, item.MaxGapSeconds);
        var group = Assert.Single(schedule.Units, unit =>
            unit.Kind == MonitorUnitKind.Group);
        Assert.Equal(120, group.MinGapSeconds);
        Assert.Equal(240, group.MaxGapSeconds);
    }

    [Fact]
    public void Group_item_gap_override_is_rejected()
    {
        var config = new MonitorConfig
        {
            Schedule = new MonitorScheduleConfig
            {
                Mode = "adaptive",
                WindowSeconds = 3600,
                MaxRequests = 20,
                MinGapSeconds = 60,
                MaxGapSeconds = 120,
            },
            Groups =
            [
                new MonitorGroupConfig
                {
                    Id = "g1",
                    Items =
                    [
                        new MonitorGroupItemConfig
                        {
                            Asin = "B000000001",
                            MinGapSeconds = 120,
                        },
                    ],
                },
            ],
        };

        var error = Assert.Throws<MonitorException>(() =>
            MonitorPlanBuilder.Build(new MonitorPlanBuildOptions
            {
                Config = config,
                DefaultDomain = "it",
            }));

        Assert.Contains("on the group", error.Message);
    }

    [Fact]
    public void Force_interval_ignores_a_schedule_block()
    {
        var warnings = new List<string>();
        var plan = MonitorPlanBuilder.Build(new MonitorPlanBuildOptions
        {
            Config = new MonitorConfig
            {
                TimerSeconds = 120,
                Schedule = new MonitorScheduleConfig
                {
                    Mode = "adaptive",
                    WindowSeconds = 3600,
                    MaxRequests = 20,
                },
                Items = [new MonitorItemConfig { Asin = "B000000001" }],
            },
            DefaultDomain = "it",
            ForceInterval = true,
            Warning = warnings.Add,
        });

        Assert.Null(plan.Schedule);
        Assert.Contains(warnings, message => message.Contains("schedule"));
    }

    [Fact]
    public void Group_with_mixed_currencies_is_rejected()
    {
        var config = new MonitorConfig
        {
            Groups =
            [
                new MonitorGroupConfig
                {
                    Id = "mixed",
                    Items =
                    [
                        new MonitorGroupItemConfig { Asin = "B000000001" },
                        new MonitorGroupItemConfig
                        {
                            Asin = "B000000002",
                            Domain = "uk",
                        },
                    ],
                },
            ],
        };

        var error = Assert.Throws<MonitorException>(() =>
            MonitorPlanBuilder.Build(new MonitorPlanBuildOptions
            {
                Config = config,
                DefaultDomain = "it",
            }));

        Assert.Contains("different currencies", error.Message);
    }

    [Fact]
    public void Same_target_in_two_groups_is_rejected()
    {
        var config = new MonitorConfig
        {
            Groups =
            [
                new MonitorGroupConfig
                {
                    Id = "one",
                    Items = [new MonitorGroupItemConfig { Asin = "B000000001" }],
                },
                new MonitorGroupConfig
                {
                    Id = "two",
                    Items = [new MonitorGroupItemConfig { Asin = "B000000001" }],
                },
            ],
        };

        var error = Assert.Throws<MonitorException>(() =>
            MonitorPlanBuilder.Build(new MonitorPlanBuildOptions
            {
                Config = config,
                DefaultDomain = "it",
            }));

        Assert.Contains("Duplicate", error.Message);
    }

    [Fact]
    public void Cli_max_price_with_mixed_currencies_is_rejected()
    {
        var error = Assert.Throws<MonitorException>(() =>
            MonitorPlanBuilder.Build(new MonitorPlanBuildOptions
            {
                CliAsins = ["B000000001", "uk:B000000002"],
                DefaultDomain = "it",
                CliMaxPrice = 100m,
            }));

        Assert.Contains("a single currency", error.Message);
    }

    [Fact]
    public void Config_hash_changes_when_group_quantity_changes()
    {
        MonitorPlan Build(int quantity)
        {
            return MonitorPlanBuilder.Build(new MonitorPlanBuildOptions
            {
                Config = new MonitorConfig
                {
                    Groups =
                    [
                        new MonitorGroupConfig
                        {
                            Id = "group",
                            Items =
                            [
                                new MonitorGroupItemConfig
                                {
                                    Asin = "B000000001",
                                    Quantity = quantity,
                                },
                            ],
                        },
                    ],
                },
                DefaultDomain = "it",
            });
        }

        var first = Build(1);
        var second = Build(2);

        Assert.NotEqual(first.ConfigHash, second.ConfigHash);
    }

    [Fact]
    public void Rise_percent_is_read_from_config_and_cli()
    {
        var configPlan = MonitorPlanBuilder.Build(new MonitorPlanBuildOptions
        {
            Config = new MonitorConfig
            {
                Items =
                [
                    new MonitorItemConfig
                    {
                        Asin = "B000000001",
                        RisePercent = 10m,
                        Baseline = "first_seen",
                    },
                ],
            },
            DefaultDomain = "it",
        });
        Assert.Equal(10m, Assert.Single(configPlan.Ungrouped).Rule.RisePercent);

        var cliPlan = MonitorPlanBuilder.Build(new MonitorPlanBuildOptions
        {
            CliAsins = ["B000000001"],
            DefaultDomain = "it",
            CliRisePercent = 5m,
        });
        Assert.Equal(5m, Assert.Single(cliPlan.Ungrouped).Rule.RisePercent);
    }

    [Fact]
    public void Rise_percent_above_one_hundred_is_rejected()
    {
        var config = new MonitorConfig
        {
            Items =
            [
                new MonitorItemConfig
                {
                    Asin = "B000000001",
                    RisePercent = 101m,
                },
            ],
        };

        Assert.Throws<MonitorException>(() =>
            MonitorPlanBuilder.Build(new MonitorPlanBuildOptions
            {
                Config = config,
                DefaultDomain = "it",
            }));
    }

    [Fact]
    public void Fixed_baseline_requires_a_price()
    {
        var config = new MonitorConfig
        {
            Items =
            [
                new MonitorItemConfig
                {
                    Asin = "B000000001",
                    DropPercent = 20m,
                    Baseline = "fixed",
                },
            ],
        };

        Assert.Throws<MonitorException>(() =>
            MonitorPlanBuilder.Build(new MonitorPlanBuildOptions
            {
                Config = config,
                DefaultDomain = "it",
            }));
    }

    [Fact]
    public void Same_asin_on_different_domains_is_not_a_duplicate()
    {
        var plan = MonitorPlanBuilder.Build(new MonitorPlanBuildOptions
        {
            CliAsins = ["B000000001", "de:B000000001"],
            DefaultDomain = "it",
        });

        Assert.Equal(2, plan.Ungrouped.Count);
        Assert.Equal(["de", "it"], plan.Domains);
    }
}
