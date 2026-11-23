using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AmazonScraper.Cli.Web;

public static class WebConsole
{
    private const int SwHide = 0;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleProcessList(
        [Out] uint[] processList,
        uint processCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>
    /// Hides the console only when this process is the sole owner. A command
    /// launched from a terminal shares the console with the shell, so hiding
    /// it would make the user's terminal disappear.
    /// </summary>
    public static bool HideOwnedConsole()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var handle = GetConsoleWindow();
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            var processes = new uint[4];
            var count = GetConsoleProcessList(processes, (uint)processes.Length);
            if (count != 1)
            {
                return false;
            }

            return ShowWindow(handle, SwHide);
        }
        catch (Exception)
        {
            return false;
        }
    }
}

public static class WebBrowser
{
    public static bool Open(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(url)
                {
                    UseShellExecute = true,
                });
                return true;
            }

            foreach (var candidate in Candidates())
            {
                if (TryStart(candidate.FileName, candidate.Arguments, url))
                {
                    return true;
                }
            }
        }
        catch (Exception)
        {
            // The caller prints the URL as a fallback.
        }

        return false;
    }

    private static IEnumerable<(string FileName, string[] Arguments)> Candidates()
    {
        var browser = Environment.GetEnvironmentVariable("BROWSER");
        if (!string.IsNullOrWhiteSpace(browser))
        {
            var first = browser.Split(':', 2)[0].Trim();
            if (first.Length > 0)
            {
                yield return (first, []);
            }
        }

        yield return ("xdg-open", []);
        yield return ("gio", ["open"]);
        yield return ("sensible-browser", []);
    }

    private static bool TryStart(string fileName, string[] prefix, string url)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in prefix)
            {
                startInfo.ArgumentList.Add(argument);
            }
            startInfo.ArgumentList.Add(url);
            using var process = Process.Start(startInfo);
            return process is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

public static class WebLog
{
    private static StreamWriter? _writer;

    public static bool RedirectToFile(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var stream = new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite);
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(
                        path,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch (Exception)
                {
                    // Best effort: the file may be on a filesystem without modes.
                }
            }
            _writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
            };
            _writer.WriteLine();
            _writer.WriteLine(
                $"=== amz-cli web {DateTimeOffset.UtcNow:O} pid {Environment.ProcessId} ===");
            Console.SetOut(_writer);
            Console.SetError(_writer);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
