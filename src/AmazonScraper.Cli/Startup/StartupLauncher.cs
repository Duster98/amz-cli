using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Startup;

/// <summary>
/// Resolves the internal `monitor --startup-run <id>` command used by systemd
/// and Task Scheduler. The actual arguments stay in the per-user startup spec,
/// so unit files and scheduled tasks never contain cookies or long command lines.
/// </summary>
public static class StartupLauncher
{
    private static readonly HashSet<string> ForbiddenArguments =
        new(StringComparer.Ordinal)
        {
            "--startup",
            "--startup-remove",
            "--startup-run",
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
            "--cycles",
            "--html",
            "--exit-on-alert",
        };

    public static bool TryPrepare(
        IReadOnlyList<string> args,
        out string[] preparedArgs,
        out int exitCode,
        StartupRegistry? registry = null)
    {
        preparedArgs = Array.Empty<string>();
        exitCode = 0;
        try
        {
            var id = ExtractId(args);
            if (id is null)
            {
                return false;
            }

            var definition = (registry ?? new StartupRegistry()).FindById(id)
                ?? throw new MonitorException(
                    $"Startup not found: {id}.");
            Validate(definition);
            if (!string.IsNullOrWhiteSpace(definition.WorkingDirectory))
            {
                if (!Directory.Exists(definition.WorkingDirectory))
                {
                    throw new MonitorException(
                        $"Startup working directory not found: " +
                        definition.WorkingDirectory);
                }

                Directory.SetCurrentDirectory(definition.WorkingDirectory);
            }

            Environment.SetEnvironmentVariable(
                "AMZ_MONITOR_STARTUP",
                definition.Kind);
            Environment.SetEnvironmentVariable(
                "AMZ_MONITOR_STARTUP_ID",
                definition.Id);
            Environment.SetEnvironmentVariable(
                "AMZ_MONITOR_STARTUP_NAME",
                definition.Name);
            preparedArgs = definition.Arguments.ToArray();
            return true;
        }
        catch (MonitorException ex)
        {
            LogError(ex.Message);
            exitCode = 1;
            return true;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or ArgumentException)
        {
            LogError(
                $"Could not start the startup: {ex.Message}");
            exitCode = 1;
            return true;
        }
    }

    private static void LogError(string message)
    {
        Console.Error.WriteLine(message);
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var root = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
            {
                root = Path.GetTempPath();
            }

            var directory = Path.Combine(root, "AmzCLI");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "startup-launch.log"),
                $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    private static string? ExtractId(IReadOnlyList<string> args)
    {
        if (args.Count < 2
            || !string.Equals(args[0], "monitor", StringComparison.Ordinal))
        {
            return null;
        }

        if (string.Equals(args[1], "--startup-run", StringComparison.Ordinal))
        {
            if (args.Count != 3 || string.IsNullOrWhiteSpace(args[2]))
            {
                throw new MonitorException(
                    "Internal use: amz-cli monitor --startup-run <id>.");
            }

            return args[2];
        }

        const string prefix = "--startup-run=";
        if (args[1].StartsWith(prefix, StringComparison.Ordinal))
        {
            if (args.Count != 2 || args[1].Length == prefix.Length)
            {
                throw new MonitorException(
                    "Internal use: amz-cli monitor --startup-run <id>.");
            }

            return args[1][prefix.Length..];
        }

        return null;
    }

    private static void Validate(StartupDefinition definition)
    {
        if (definition.Arguments.Count == 0
            || !string.Equals(
                definition.Arguments[0],
                "monitor",
                StringComparison.Ordinal))
        {
            throw new MonitorException(
                $"Startup '{definition.Id}' has no valid monitor command.");
        }

        foreach (var argument in definition.Arguments)
        {
            if (ForbiddenArguments.Contains(argument))
            {
                throw new MonitorException(
                    $"Startup '{definition.Id}' contains the invalid flag " +
                    $"'{argument}'.");
            }
        }
    }
}
