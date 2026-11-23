using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Startup;

public sealed record StartupLaunchCommand(
    string Executable,
    IReadOnlyList<string> PrefixArguments);

public static class StartupDefinitionFactory
{
    private static readonly HashSet<string> ControlFlags = new(StringComparer.Ordinal)
    {
        "--startup",
        "--startup-remove",
        "--detach",
        "--status",
        "--stop",
        "--list",
        "--all",
        "--prune",
        "--json",
        "--tk-opt",
        "--verbose",
        "--notify-test",
        "--dry-run",
        "--once",
        "--exit-on-alert",
    };

    private static readonly HashSet<string> ControlFlagsWithValue =
        new(StringComparer.Ordinal)
        {
            "--cycles",
            "--html",
            "--startup-run",
        };

    private static readonly string[] SecretOptions =
    [
        "--cookie",
        "--domain-cookie",
    ];

    public static StartupDefinition Create(
        MonitorPaths paths,
        MonitorPlan plan,
        MonitorFormat format,
        MonitorLayout layout,
        int timerSeconds,
        string? configPath,
        string? logPath,
        IReadOnlyList<string> commandLine)
    {
        var launch = ResolveLaunchCommand();
        var effectiveLogPath = Path.GetFullPath(
            string.IsNullOrWhiteSpace(logPath) ? paths.LogPath : logPath);
        var arguments = BuildArguments(commandLine, effectiveLogPath);
        var savePath = StartupIdentity.NormalizePath(paths.SavePath);
        return new StartupDefinition
        {
            Id = StartupIdentity.CreateId(savePath, paths.BaseName),
            CreatedAt = MonitorObservationFactory.FormatTimestamp(
                DateTimeOffset.UtcNow),
            Executable = launch.Executable,
            PrefixArguments = launch.PrefixArguments.ToList(),
            WorkingDirectory = StartupIdentity.NormalizePath(
                Environment.CurrentDirectory),
            ConfigPath = string.IsNullOrWhiteSpace(configPath)
                ? null
                : Path.GetFullPath(configPath),
            BindingId = FindArgumentValue(commandLine, "--binding"),
            SavePath = savePath,
            SaveFile = paths.BaseName,
            Domains = plan.Domains.ToList(),
            Targets = plan.AllTargets.Count(),
            TimerSeconds = timerSeconds,
            ScheduleMode = plan.Schedule is null ? null : "adaptive",
            WindowSeconds = plan.Schedule?.WindowSeconds,
            MaxRequests = plan.Schedule?.MaxRequests,
            MinGapSeconds = plan.Schedule?.DefaultMinGapSeconds,
            MaxGapSeconds = plan.Schedule?.DefaultMaxGapSeconds,
            DataFiles = MonitorOutput.DescribeFiles(
                    paths,
                    format,
                    layout,
                    plan)
                .ToList(),
            LogPath = effectiveLogPath,
            Arguments = arguments,
            CommandPreview = "amz-cli " + RedactCommand(arguments),
        };
    }

    private static string? FindArgumentValue(
        IReadOnlyList<string> arguments,
        string name)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (string.Equals(argument, name, StringComparison.Ordinal)
                && index + 1 < arguments.Count)
            {
                return arguments[index + 1];
            }
            if (argument.StartsWith($"{name}=", StringComparison.Ordinal))
            {
                return argument[(name.Length + 1)..];
            }
        }
        return null;
    }

    public static StartupLaunchCommand ResolveLaunchCommand()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new MonitorException(
                "Could not determine the executable for --startup.");
        }

        var executable = Path.GetFullPath(processPath);
        var name = Path.GetFileNameWithoutExtension(executable);
        if (!string.Equals(name, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return new StartupLaunchCommand(executable, []);
        }

        var assembly = typeof(StartupDefinitionFactory).Assembly.Location;
        if (string.IsNullOrWhiteSpace(assembly))
        {
            throw new MonitorException(
                "--startup requires the published binary or an assembly " +
                "on disk when using 'dotnet'.");
        }

        return new StartupLaunchCommand(
            executable,
            [Path.GetFullPath(assembly)]);
    }

    public static List<string> BuildArguments(
        IReadOnlyList<string> commandLine,
        string defaultLogPath)
    {
        var args = commandLine.ToList();
        if (args.Count > 0
            && !string.Equals(args[0], "monitor", StringComparison.Ordinal))
        {
            args.RemoveAt(0);
        }

        if (args.Count == 0
            || !string.Equals(args[0], "monitor", StringComparison.Ordinal))
        {
            throw new MonitorException(
                "The --startup command line must start with 'monitor'.");
        }

        var result = new List<string>();
        var hasLogPath = false;
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (ControlFlags.Contains(argument))
            {
                continue;
            }

            if (ControlFlagsWithValue.Contains(argument))
            {
                index++;
                continue;
            }

            if (argument.StartsWith("--startup-run=", StringComparison.Ordinal)
                || argument.StartsWith("--cycles=", StringComparison.Ordinal)
                || argument.StartsWith("--html=", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(argument, "--log-path", StringComparison.Ordinal))
            {
                hasLogPath = true;
            }
            else if (argument.StartsWith("--log-path=", StringComparison.Ordinal))
            {
                if (argument.Length > "--log-path=".Length)
                {
                    hasLogPath = true;
                }
                else
                {
                    continue;
                }
            }

            result.Add(argument);
        }

        if (!hasLogPath)
        {
            result.Add("--log-path");
            result.Add(Path.GetFullPath(defaultLogPath));
        }

        return result;
    }

    public static string RedactCommand(IReadOnlyList<string> arguments)
    {
        var parts = new List<string>(arguments.Count);
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (SecretOptions.Contains(argument))
            {
                parts.Add(argument);
                if (index + 1 < arguments.Count)
                {
                    index++;
                    parts.Add("***");
                }
                continue;
            }

            var secret = SecretOptions.FirstOrDefault(option =>
                argument.StartsWith(option + "=", StringComparison.Ordinal));
            if (secret is not null)
            {
                parts.Add($"{secret}=***");
                continue;
            }

            parts.Add(QuotePreview(argument));
        }

        return string.Join(' ', parts);
    }

    private static string QuotePreview(string value) =>
        value.Length > 0
        && value.All(character =>
            !char.IsWhiteSpace(character)
            && character != '"'
            && character != '\'')
            ? value
            : $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";
}
