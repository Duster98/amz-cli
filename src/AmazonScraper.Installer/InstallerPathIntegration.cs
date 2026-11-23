using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace AmazonScraper.Installer;

public static class InstallerPaths
{
    public static string DefaultInstallDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(local))
            {
                local = Path.GetTempPath();
            }

            return Path.Combine(local, "Programs", "AmzCLI");
        }

        if (OperatingSystem.IsLinux())
        {
            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrWhiteSpace(dataHome))
            {
                return Path.Combine(dataHome, "amz-cli");
            }

            var home = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
            {
                return Path.Combine(home, ".local", "share", "amz-cli");
            }
        }

        return Path.Combine(Path.GetTempPath(), "amz-cli");
    }

    public static string ResolveInstallDirectory(string? requested)
    {
        var path = string.IsNullOrWhiteSpace(requested)
            ? DefaultInstallDirectory()
            : requested.Trim();
        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
            {
                path = path == "~"
                    ? home
                    : Path.Combine(home, path[2..]);
            }
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    public static string LinuxBinDirectory()
    {
        var binHome = Environment.GetEnvironmentVariable("XDG_BIN_HOME");
        if (!string.IsNullOrWhiteSpace(binHome))
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(binHome));
        }

        var home = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(home)
            ? Path.Combine(Path.GetTempPath(), "amz-cli-bin")
            : Path.Combine(home, ".local", "bin");
    }
}

public static class InstallerPathIntegration
{
    private const string WindowsShortcutName = "AMZ CLI.lnk";
    private const string LegacyWindowsShortcutName = "Amazon.lnk";

    public static void CreateLinuxSymlink(
        string installDir,
        bool force,
        bool quiet)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var executable = Path.Combine(installDir, "amz-cli");
        var directory = InstallerPaths.LinuxBinDirectory();
        var link = Path.Combine(directory, "amz-cli");
        Directory.CreateDirectory(directory);

        if (File.Exists(link) || Directory.Exists(link))
        {
            var existing = TryResolveLink(link);
            if (string.Equals(
                    existing,
                    Path.GetFullPath(executable),
                    StringComparison.Ordinal))
            {
                return;
            }
            if (!force)
            {
                Warn(
                    quiet,
                    $"The symlink '{link}' already exists and points to another path; " +
                    "use --force to replace it.");
                return;
            }
            File.Delete(link);
        }

        File.CreateSymbolicLink(link, executable);
        if (!quiet)
        {
            Console.WriteLine($"Symlink: {link}");
        }
    }

    public static void RemoveLinuxSymlink(string installDir)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var executable = Path.GetFullPath(Path.Combine(installDir, "amz-cli"));
        var link = Path.Combine(InstallerPaths.LinuxBinDirectory(), "amz-cli");
        if (!File.Exists(link))
        {
            return;
        }

        if (string.Equals(
                TryResolveLink(link),
                executable,
                StringComparison.Ordinal))
        {
            File.Delete(link);
        }
    }

    public static void AddWindowsPath(string installDir, bool quiet)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var executable = Path.Combine(installDir, "amz-cli.exe");
        if (!File.Exists(executable))
        {
            return;
        }

        if (!RunCommand(executable, ["--install-env-var"], out var output))
        {
            Warn(quiet, $"Unable to update the user PATH: {output}");
            return;
        }

        if (!quiet)
        {
            Console.WriteLine("User PATH updated. Reopen the terminal.");
        }
    }

    public static void RemoveWindowsPath(string installDir, bool quiet)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var executable = Path.Combine(installDir, "amz-cli.exe");
        if (!File.Exists(executable))
        {
            return;
        }

        if (!RunCommand(executable, ["--uninstall-env-var"], out var output))
        {
            Warn(quiet, $"Unable to remove the user PATH: {output}");
        }
    }

    public static void CreateApplicationShortcuts(string installDir, bool quiet)
    {
        if (OperatingSystem.IsWindows())
        {
            CreateWindowsShortcuts(installDir, quiet);
            return;
        }
        if (OperatingSystem.IsLinux())
        {
            CreateLinuxDesktopEntry(installDir, quiet);
        }
    }

    public static void RemoveApplicationShortcuts()
    {
        if (OperatingSystem.IsWindows())
        {
            var startMenu = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft",
                "Windows",
                "Start Menu",
                "Programs");
            TryDeleteFile(Path.Combine(startMenu, WindowsShortcutName));
            TryDeleteFile(Path.Combine(startMenu, LegacyWindowsShortcutName));
            var desktop = Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory);
            if (!string.IsNullOrWhiteSpace(desktop))
            {
                TryDeleteFile(Path.Combine(desktop, WindowsShortcutName));
                TryDeleteFile(Path.Combine(desktop, LegacyWindowsShortcutName));
            }
            return;
        }

        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        TryDeleteFile(LinuxDesktopEntryPath());
        TryDeleteFile(LegacyLinuxDesktopEntryPath());
    }

    [SupportedOSPlatform("windows")]
    private static void CreateWindowsShortcuts(string installDir, bool quiet)
    {
        var executable = Path.Combine(installDir, "amz-cli.exe");
        if (!File.Exists(executable))
        {
            return;
        }

        var startMenu = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft",
            "Windows",
            "Start Menu",
            "Programs");
        Directory.CreateDirectory(startMenu);
        var created = CreateWindowsShortcut(
            Path.Combine(startMenu, WindowsShortcutName),
            executable,
            installDir);
        TryDeleteFile(Path.Combine(startMenu, LegacyWindowsShortcutName));
        var desktop = Environment.GetFolderPath(
            Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrWhiteSpace(desktop))
        {
            created |= CreateWindowsShortcut(
                Path.Combine(desktop, WindowsShortcutName),
                executable,
                installDir);
            TryDeleteFile(Path.Combine(desktop, LegacyWindowsShortcutName));
        }

        if (!created)
        {
            Warn(
                quiet,
                "unable to create the AMZ CLI shortcut; use 'amz-cli web --open'.");
        }
        else if (!quiet)
        {
            Console.WriteLine("AMZ CLI shortcuts created.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool CreateWindowsShortcut(
        string linkPath,
        string executable,
        string installDir)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null)
            {
                return false;
            }

            shell = Activator.CreateInstance(type);
            if (shell is null)
            {
                return false;
            }

            dynamic shellApi = shell;
            shortcut = shellApi.CreateShortcut(linkPath);
            dynamic link = shortcut;
            link.TargetPath = executable;
            link.Arguments = "web --desktop --open";
            link.WorkingDirectory = installDir;
            link.IconLocation = executable + ",0";
            link.Description = "AMZ CLI - search and product details";
            link.WindowStyle = 1;
            link.Save();
            return File.Exists(linkPath);
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            try
            {
                if (shortcut is not null)
                {
                    Marshal.FinalReleaseComObject(shortcut);
                }
                if (shell is not null)
                {
                    Marshal.FinalReleaseComObject(shell);
                }
            }
            catch (Exception)
            {
                // COM objects can already be released by a failed Save().
            }
        }
    }

    private static void CreateLinuxDesktopEntry(string installDir, bool quiet)
    {
        var executable = Path.Combine(installDir, "amz-cli");
        if (!File.Exists(executable))
        {
            return;
        }

        try
        {
            var path = LinuxDesktopEntryPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            TryDeleteFile(LegacyLinuxDesktopEntryPath());
            var content = new StringBuilder()
                .AppendLine("[Desktop Entry]")
                .AppendLine("Type=Application")
                .AppendLine("Version=1.0")
                .AppendLine("Name=AMZ CLI")
                .AppendLine("Comment=Amazon search and product details")
                .AppendLine($"Exec=\"{executable}\" web --desktop")
                .AppendLine("Icon=system-search")
                .AppendLine("Terminal=false")
                .AppendLine("Categories=Utility;")
                .AppendLine("StartupNotify=false")
                .ToString();
            File.WriteAllText(path, content);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.GroupRead
                    | UnixFileMode.OtherRead);
            }

            if (!quiet)
            {
                Console.WriteLine($"Application entry: {path}");
            }
        }
        catch (Exception ex)
        {
            Warn(quiet, $"unable to create the application entry: {ex.Message}");
        }
    }

    private static string LinuxDesktopEntryPath() =>
        LinuxApplicationsDirectory() is { } directory
            ? Path.Combine(directory, "amz-cli.desktop")
            : Path.Combine(Path.GetTempPath(), "amz-cli.desktop");

    private static string LegacyLinuxDesktopEntryPath() =>
        LinuxApplicationsDirectory() is { } directory
            ? Path.Combine(directory, "amazon.desktop")
            : Path.Combine(Path.GetTempPath(), "amazon.desktop");

    private static string? LinuxApplicationsDirectory()
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(dataHome))
        {
            return Path.Combine(dataHome, "applications");
        }

        var home = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(home)
            ? null
            : Path.Combine(home, ".local", "share", "applications");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    private static string? TryResolveLink(string path)
    {
        try
        {
            var target = File.ResolveLinkTarget(path, returnFinalTarget: true);
            return target is null
                ? null
                : Path.GetFullPath(target.FullName);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool RunCommand(
        string executable,
        IReadOnlyList<string> arguments,
        out string output)
    {
        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executable)
                    ?? Environment.CurrentDirectory,
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                output = "startup failed";
                return false;
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(30_000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                    // Best effort.
                }
                output = "timeout";
                return false;
            }

            output = string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            output = ex.Message;
            return false;
        }
    }

    private static void Warn(bool quiet, string message)
    {
        if (!quiet)
        {
            Console.Error.WriteLine($"Warning: {message}");
        }
    }
}
