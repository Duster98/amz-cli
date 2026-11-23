using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace AmazonScraper.Cli.EnvSetup;

public sealed record EnvVarResult(bool Success, string Message);

[SupportedOSPlatform("windows")]
public static class WindowsPathInstaller
{
    private const string UserKey = "Environment";
    private const string MachineKey =
        @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";

    public static EnvVarResult Install(bool system) => Update(system, install: true);

    public static EnvVarResult Uninstall(bool system) => Update(system, install: false);

    public static bool IsExecutableDirectoryInPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var directory = ExecutableDirectory();
        var userPath = ReadPath(system: false, out _) ?? string.Empty;
        var machinePath = ReadPath(system: true, out _) ?? string.Empty;
        return PathList.Contains(userPath, directory)
            || PathList.Contains(machinePath, directory);
    }

    public static EnvVarResult Status()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new EnvVarResult(
                false,
                "PATH management is only available on Windows.");
        }

        var directory = ExecutableDirectory();
        var userPath = ReadPath(system: false, out var userError) ?? string.Empty;
        var machinePath = ReadPath(system: true, out var machineError) ?? string.Empty;
        var lines = new List<string>
        {
            $"Directory: {directory}",
            $"User PATH: {YesNo(PathList.Contains(userPath, directory))}",
            $"System PATH: {YesNo(PathList.Contains(machinePath, directory))}",
        };

        if (userError is not null)
        {
            lines.Add($"User PATH unreadable: {userError}");
        }
        if (machineError is not null)
        {
            lines.Add($"System PATH unreadable: {machineError}");
        }

        lines.Add(string.Empty);
        lines.Add("where amz-cli (current session):");
        var where = RunWhere();
        lines.AddRange(where.Count == 0
            ? ["  not found"]
            : where.Select(line => $"  {line}"));

        if ((PathList.Contains(userPath, directory)
             || PathList.Contains(machinePath, directory))
            && where.Count == 0)
        {
            lines.Add(
                "The entry is in PATH but not in the current session: " +
                "reopen the terminal.");
        }

        return new EnvVarResult(true, string.Join(Environment.NewLine, lines));
    }

    private static EnvVarResult Update(bool system, bool install)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new EnvVarResult(
                false,
                "PATH management is only available on Windows.");
        }

        var directory = ExecutableDirectory();
        if (install
            && !File.Exists(Path.Combine(directory, "amz-cli.exe")))
        {
            return new EnvVarResult(
                false,
                $"There is no amz-cli.exe in this directory: {directory}{Environment.NewLine}" +
                "Run the command from the published binary directory.");
        }

        var current = ReadPath(system, out var readError);
        if (readError is not null)
        {
            return new EnvVarResult(false, readError);
        }

        var scope = system ? "system" : "user";
        var updatedPath = install
            ? PathList.Add(current, directory, out var changed)
            : PathList.Remove(current, directory, out changed);
        if (!changed)
        {
            return new EnvVarResult(
                true,
                install
                    ? $"The directory is already in the {scope} PATH.{Environment.NewLine}" +
                      $"No changes: {directory}"
                    : $"The directory was not in the {scope} PATH.{Environment.NewLine}" +
                      $"No changes: {directory}");
        }

        if (!WritePath(system, updatedPath, out var writeError))
        {
            var hint = system
                ? "Start the terminal as administrator to modify the system PATH."
                : "Check permissions on the user PATH.";
            return new EnvVarResult(
                false,
                $"{writeError}{Environment.NewLine}{hint}");
        }

        BroadcastEnvironmentChange();
        return new EnvVarResult(
            true,
            install
                ? $"The {scope} PATH was updated.{Environment.NewLine}" +
                  $"Added: {directory}{Environment.NewLine}" +
                  "Reopen the terminal to use `amz-cli` anywhere."
                : $"The {scope} PATH was updated.{Environment.NewLine}" +
                  $"Removed: {directory}{Environment.NewLine}" +
                  "Reopen the terminal to update the session.");
    }

    private static string ExecutableDirectory()
    {
        var processPath = Environment.ProcessPath;
        var processName = processPath is null
            ? null
            : Path.GetFileNameWithoutExtension(processPath);
        var directory = processPath is null
            ? null
            : Path.GetDirectoryName(processPath);

        if (string.IsNullOrWhiteSpace(directory)
            || string.Equals(processName, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            directory = AppContext.BaseDirectory;
        }

        return Path.GetFullPath(directory ?? AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string? ReadPath(bool system, out string? error)
    {
        error = null;
        try
        {
            using var key = system
                ? Registry.LocalMachine.OpenSubKey(MachineKey, writable: false)
                : Registry.CurrentUser.OpenSubKey(UserKey, writable: false);
            if (key is null)
            {
                return string.Empty;
            }

            return key.GetValue(
                    "Path",
                    string.Empty,
                    RegistryValueOptions.DoNotExpandEnvironmentNames)
                as string
                ?? string.Empty;
        }
        catch (Exception ex) when (
            ex is SecurityException
                or UnauthorizedAccessException
                or IOException)
        {
            error = $"Could not read the {(system ? "system" : "user")} PATH: {ex.Message}";
            return null;
        }
    }

    private static bool WritePath(bool system, string value, out string? error)
    {
        error = null;
        try
        {
            using var key = system
                ? Registry.LocalMachine.OpenSubKey(MachineKey, writable: true)
                : Registry.CurrentUser.CreateSubKey(UserKey, writable: true);
            if (key is null)
            {
                error = system
                    ? "System PATH key not found."
                    : "User PATH key not found.";
                return false;
            }

            key.SetValue("Path", value, RegistryValueKind.ExpandString);
            return true;
        }
        catch (Exception ex) when (
            ex is SecurityException
                or UnauthorizedAccessException
                or IOException)
        {
            error = $"Could not write the {(system ? "system" : "user")} PATH: {ex.Message}";
            return false;
        }
    }

    private static List<string> RunWhere()
    {
        try
        {
            var startInfo = new ProcessStartInfo("where.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("amz-cli");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return [];
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            return output
                .Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries)
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static void BroadcastEnvironmentChange()
    {
        try
        {
            _ = SendMessageTimeout(
                new IntPtr(0xffff),
                0x001A,
                UIntPtr.Zero,
                "Environment",
                0x0002,
                5000,
                out _);
        }
        catch (Exception)
        {
            // The registry update is already done; a new terminal will see it.
        }
    }

    private static string YesNo(bool value) => value ? "present" : "absent";

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        UIntPtr wParam,
        string lParam,
        uint flags,
        uint timeout,
        out UIntPtr result);
}
