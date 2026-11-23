using System.Text;
using AmazonScraper.Update;

namespace AmazonScraper.Installer;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(false);
        }
        catch (IOException)
        {
            // Some consoles do not allow changing the encoding.
        }

        InstallerOptions options;
        try
        {
            options = InstallerOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            PrintHelp();
            return 1;
        }

        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }
        if (options.ShowVersion)
        {
            Console.WriteLine(InstallerInfo.Display);
            return 0;
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            var service = new InstallService();
            var progress = new Progress<UpdateProgress>(item =>
            {
                if (options.Quiet)
                {
                    return;
                }

                var percent = item.Percent is { } value
                    ? $" {value}%"
                    : string.Empty;
                Console.WriteLine($"[{item.Stage}]{percent} {item.Message}");
            });

            InstallResult result;
            if (options.Uninstall)
            {
                if (!options.Yes && !options.DryRun)
                {
                    if (Console.IsInputRedirected)
                    {
                        throw new UpdateException(
                            "Non-interactive input: use --yes to uninstall.");
                    }

                    Console.Write(
                        "Remove the AMZ CLI installation? [y/N] ");
                    var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
                    if (answer is not ("y" or "yes" or "s" or "si" or "sì"))
                    {
                        Console.WriteLine("Operation cancelled.");
                        return 0;
                    }
                }

                result = await service.UninstallAsync(
                        options,
                        cancellation.Token)
                    .ConfigureAwait(false);
                if (!options.Quiet)
                {
                    Console.WriteLine(
                        $"Installation removed: {result.InstallDir}");
                    Console.WriteLine(
                        "Monitor data and user state were preserved.");
                }
            }
            else
            {
                result = await service.InstallLatestAsync(
                        options,
                        progress,
                        cancellation.Token)
                    .ConfigureAwait(false);
                if (result.Cancelled)
                {
                    Console.WriteLine("Installation cancelled.");
                    return 0;
                }
                if (options.DryRun)
                {
                    if (!options.Quiet)
                    {
                        Console.WriteLine("Dry-run completed.");
                    }
                    return 0;
                }
                if (!options.Quiet)
                {
                    if (result.AlreadyInstalled)
                    {
                        Console.WriteLine(
                            $"AMZ CLI {result.Version} is already installed in " +
                            $"{result.InstallDir}.");
                    }
                    else
                    {
                        Console.WriteLine(
                            $"Installation completed: AMZ CLI {result.Version}");
                        Console.WriteLine(
                            $"Comando: {Path.Combine(result.InstallDir, PlatformInfo.ExecutableName(result.Platform))}");
                    }
                }
            }

            return result.Success ? 0 : 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operation cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            AMZ CLI installer

            Usage:
              amz-cli-installer [options]

            Options:
              --dir <PATH>        installation directory
              --channel <CHANNEL> stable (default) or beta
              --no-path           do not create symlinks, PATH entries, or app shortcuts
              --force             reinstall or replace an existing installation
              --dry-run           show the plan without downloading or writing
              --yes, -y           do not ask for confirmation
              --quiet             reduced output
              --uninstall         remove installed files
              --purge             with --uninstall, also remove the directory
              --version           show the installer version
              --help, -h          show this help

            Default directories:
              Linux:   $XDG_DATA_HOME/amz-cli or ~/.local/share/amz-cli
              Windows: %LOCALAPPDATA%\Programs\AmzCLI
            """);
    }
}
