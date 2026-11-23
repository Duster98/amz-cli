namespace AmazonScraper.Installer;

public sealed class InstallerOptions
{
    public string? Directory { get; private init; }

    public string Channel { get; private init; } = "stable";

    public bool NoPath { get; private init; }

    public bool Force { get; private init; }

    public bool DryRun { get; private init; }

    public bool Yes { get; private init; }

    public bool Quiet { get; private init; }

    public bool Uninstall { get; private init; }

    public bool Purge { get; private init; }

    public bool ShowHelp { get; private init; }

    public bool ShowVersion { get; private init; }

    public static InstallerOptions Parse(IReadOnlyList<string> args)
    {
        string? directory = null;
        var channel = "stable";
        var noPath = false;
        var force = false;
        var dryRun = false;
        var yes = false;
        var quiet = false;
        var uninstall = false;
        var purge = false;
        var help = false;
        var version = false;

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--dir":
                    directory = RequireValue(args, ref index, "--dir");
                    break;
                case "--channel":
                    channel = RequireValue(args, ref index, "--channel");
                    break;
                case "--no-path":
                    noPath = true;
                    break;
                case "--force":
                    force = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--yes":
                case "-y":
                    yes = true;
                    break;
                case "--quiet":
                    quiet = true;
                    break;
                case "--uninstall":
                    uninstall = true;
                    break;
                case "--purge":
                    purge = true;
                    break;
                case "--help":
                case "-h":
                    help = true;
                    break;
                case "--version":
                    version = true;
                    break;
                default:
                    if (argument.StartsWith("--dir=", StringComparison.Ordinal))
                    {
                        directory = argument["--dir=".Length..];
                    }
                    else if (argument.StartsWith("--channel=", StringComparison.Ordinal))
                    {
                        channel = argument["--channel=".Length..];
                    }
                    else
                    {
                        throw new ArgumentException(
                            $"Unrecognized option: '{argument}'. Use --help.");
                    }
                    break;
            }
        }

        channel = channel.Trim().ToLowerInvariant();
        if (channel is not ("stable" or "beta"))
        {
            throw new ArgumentException("--channel must be stable or beta.");
        }
        if (purge && !uninstall)
        {
            throw new ArgumentException("--purge requires --uninstall.");
        }
        if (!string.IsNullOrWhiteSpace(directory))
        {
            directory = directory.Trim();
        }

        return new InstallerOptions
        {
            Directory = directory,
            Channel = channel,
            NoPath = noPath,
            Force = force,
            DryRun = dryRun,
            Yes = yes,
            Quiet = quiet,
            Uninstall = uninstall,
            Purge = purge,
            ShowHelp = help,
            ShowVersion = version,
        };
    }

    private static string RequireValue(
        IReadOnlyList<string> args,
        ref int index,
        string option)
    {
        if (index + 1 >= args.Count
            || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            throw new ArgumentException($"{option} richiede un valore.");
        }

        return args[++index].Trim();
    }
}
