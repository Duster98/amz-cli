using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CommunityToolkit.WinUI.Notifications;

namespace AmazonScraper.Notifications.Windows;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch (Exception)
        {
            // Some console hosts do not allow changing the encoding.
        }

        var startupIndex = Array.IndexOf(args, "--startup-launch");
        if (startupIndex >= 0)
        {
            if (startupIndex + 1 >= args.Length)
            {
                WriteStartupError("--startup-launch richiede un id.");
                return 1;
            }

            return LaunchStartup(args[startupIndex + 1]);
        }

        var viewIndex = Array.IndexOf(args, "--view");
        if (viewIndex >= 0 && viewIndex + 1 < args.Length)
        {
            EnsureConsole();
            return NotificationViewer.Show(args[viewIndex + 1]);
        }

        string? activatedPayload = null;
        using var activated = new ManualResetEventSlim(false);
        ToastNotificationManagerCompat.OnActivated += eventArgs =>
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(eventArgs.Argument))
                {
                    var arguments = ToastArguments.Parse(eventArgs.Argument);
                    activatedPayload = arguments.TryGetValue("payload", out var payload)
                        ? payload
                        : null;
                }
            }
            catch (Exception)
            {
                activatedPayload = null;
            }

            activated.Set();
        };

        if (WasToastActivated(args))
        {
            activated.Wait(TimeSpan.FromSeconds(4));
            OpenActivatedViewer(activatedPayload);
            return 0;
        }

        var title = Environment.GetEnvironmentVariable("AMZ_MONITOR_NOTIFY_TITLE")
            ?? "AMZ CLI";
        var body = Environment.GetEnvironmentVariable("AMZ_MONITOR_NOTIFY_BODY")
            ?? string.Empty;
        var payloadPath = Environment.GetEnvironmentVariable("AMZ_MONITOR_NOTIFY_PAYLOAD");

        try
        {
            var builder = new ToastContentBuilder()
                .AddText(title)
                .AddText(body);
            if (!string.IsNullOrWhiteSpace(payloadPath))
            {
                builder.AddArgument("payload", payloadPath);
            }

            builder.Show();
            Console.Out.WriteLine("communitytoolkit");
            Thread.Sleep(300);
            return 0;
        }
        catch (Exception ex)
        {
            WriteHelperError("notify.log", $"Notification not sent: {ex.Message}");
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int LaunchStartup(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            WriteStartupError("Id startup vuoto.");
            return 1;
        }

        var executable = Path.Combine(
            AppContext.BaseDirectory,
            "amz-cli.exe");
        if (!File.Exists(executable))
        {
            WriteStartupError(
                $"amz-cli.exe not found next to the helper: {executable}");
            return 1;
        }

        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Environment.CurrentDirectory,
            };
            startInfo.ArgumentList.Add("monitor");
            startInfo.ArgumentList.Add("--startup-run");
            startInfo.ArgumentList.Add(id);
            var process = Process.Start(startInfo);
            if (process is null)
            {
                WriteStartupError(
                    $"Failed to start '{executable}'.");
                return 1;
            }

            using (process)
            {
                if (process.WaitForExit(1500)
                    && process.ExitCode != 0)
                {
                    WriteStartupError(
                        $"amz-cli.exe exited immediately with code " +
                        $"{process.ExitCode} (startup {id}).");
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            WriteStartupError(
                $"Failed to start startup '{id}': {ex.Message}");
            return 1;
        }
    }

    private static void WriteStartupError(string message)
        => WriteHelperError("startup-launch.log", message);

    private static void WriteHelperError(string fileName, string message)
    {
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
                Path.Combine(directory, fileName),
                $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // There is no console in a scheduled-task launch; best effort.
        }
    }

    private static void EnsureConsole()
    {
        if (GetConsoleWindow() != IntPtr.Zero)
        {
            return;
        }

        if (!AllocConsole())
        {
            WriteHelperError(
                "notify.log",
                "Unable to allocate the console for the viewer.");
            return;
        }

        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            Console.SetOut(
                new StreamWriter(Console.OpenStandardOutput(), utf8)
                {
                    AutoFlush = true,
                });
            Console.SetError(
                new StreamWriter(Console.OpenStandardError(), utf8)
                {
                    AutoFlush = true,
                });
            Console.SetIn(new StreamReader(Console.OpenStandardInput()));
            Console.OutputEncoding = utf8;
        }
        catch (Exception ex)
        {
            WriteHelperError(
                "notify.log",
                $"Viewer console configuration failed: {ex.Message}");
        }
    }

    private static bool WasToastActivated(string[] args)
    {
        try
        {
            if (ToastNotificationManagerCompat.WasCurrentProcessToastActivated())
            {
                return true;
            }
        }
        catch (Exception)
        {
            // Fall through to the command-line marker.
        }

        return args.Any(argument =>
            argument.Contains("ToastActivated", StringComparison.OrdinalIgnoreCase));
    }

    private static void OpenActivatedViewer(string? payloadPath)
    {
        var console = GetConsoleWindow();
        if (console != IntPtr.Zero)
        {
            // The toast activation launched a console helper; bring it forward
            // and render the detail in the same window.
            ShowWindow(console, 5);
            SetForegroundWindow(console);
            NotificationViewer.Show(payloadPath);
            return;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            NotificationViewer.Show(payloadPath);
            return;
        }

        // A notification activation has no console in most cases. `start`
        // creates a console window for the console-subsystem helper.
        try
        {
            var startInfo = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("start");
            startInfo.ArgumentList.Add("AMZ CLI");
            startInfo.ArgumentList.Add(executable);
            startInfo.ArgumentList.Add("--view");
            startInfo.ArgumentList.Add(payloadPath ?? string.Empty);
            Process.Start(startInfo);
            return;
        }
        catch (Exception)
        {
            // Fall through to ShellExecute.
        }

        var arguments = string.IsNullOrWhiteSpace(payloadPath)
            ? "--view \"\""
            : $"--view \"{payloadPath}\"";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            NotificationViewer.Show(payloadPath);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
