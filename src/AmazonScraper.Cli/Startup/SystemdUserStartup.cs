using System.Text;
using AmazonScraper.Cli.Storage;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Startup;

public sealed class SystemdUserStartup : IStartupBackend
{
    public const string KindName = "systemd-user";

    private readonly IStartupProcessRunner _runner;
    private readonly string? _unitDirectory;

    public SystemdUserStartup(
        IStartupProcessRunner? runner = null,
        string? unitDirectory = null)
    {
        _runner = runner ?? new StartupProcessRunner();
        _unitDirectory = unitDirectory;
    }

    public string Kind => KindName;

    public string UnitDirectory =>
        _unitDirectory
        ?? UserStatePaths.SystemdUserDirectory()
        ?? throw new MonitorException(
            "Could not determine ~/.config/systemd/user.");

    public string NameFor(string id) => $"amz-monitor-{id}.service";

    public bool IsAvailable(out string? error)
    {
        if (!OperatingSystem.IsLinux())
        {
            error = "systemd is only available on Linux.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(UserStatePaths.SystemdUserDirectory()))
        {
            error = "Could not determine the user systemd directory.";
            return false;
        }

        try
        {
            var result = Run("show-environment");
            if (!result.Success)
            {
                error =
                    "systemd user is not available in this session: " +
                    result.Detail();
                return false;
            }
        }
        catch (MonitorException ex)
        {
            error = ex.Message;
            return false;
        }

        error = null;
        return true;
    }

    public void Install(StartupDefinition definition)
    {
        var directory = UnitDirectory;
        Directory.CreateDirectory(directory);
        var path = UnitPath(definition);
        WriteAtomic(path, BuildUnit(definition));

        var reload = Run("daemon-reload");
        if (!reload.Success)
        {
            throw new MonitorException(
                $"systemctl --user daemon-reload failed: {reload.Detail()}");
        }

        var enable = Run("enable", UnitName(definition));
        if (!enable.Success)
        {
            throw new MonitorException(
                $"systemctl --user enable failed: {enable.Detail()}");
        }
    }

    public void Start(StartupDefinition definition)
    {
        var result = Run("start", UnitName(definition));
        if (!result.Success)
        {
            throw new MonitorException(
                $"systemctl --user start failed: {result.Detail()}");
        }
    }

    public void Stop(StartupDefinition definition)
    {
        _ = Run("stop", UnitName(definition));
    }

    public void Restart(StartupDefinition definition)
    {
        var result = Run("restart", UnitName(definition));
        if (!result.Success)
        {
            throw new MonitorException(
                $"systemctl --user restart failed: {result.Detail()}");
        }
    }

    public bool Remove(StartupDefinition definition)
    {
        var path = UnitPath(definition);
        var existed = File.Exists(path);
        var disabled = false;
        try
        {
            disabled = Run("disable", "--now", UnitName(definition)).Success;
        }
        catch (MonitorException)
        {
            // The user manager may be unavailable; the unit file can still be
            // removed so the startup does not come back at the next login.
        }

        if (File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                throw new MonitorException(
                    $"Could not remove unit '{path}': {ex.Message}");
            }
        }

        try
        {
            Run("daemon-reload");
            Run("reset-failed", UnitName(definition));
        }
        catch (MonitorException)
        {
            // Best effort.
        }

        return existed || disabled;
    }

    public bool Exists(StartupDefinition definition) =>
        File.Exists(UnitPath(definition));

    public static string BuildUnit(StartupDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Executable))
        {
            throw new MonitorException(
                "systemd startup without an executable.");
        }

        var command = new List<string>
        {
            definition.Executable,
        };
        command.AddRange(definition.PrefixArguments);
        command.Add("monitor");
        command.Add("--startup-run");
        command.Add(definition.Id);

        var description = new string(
                (definition.SaveFile.Length == 0
                        ? "amz-monitor"
                        : definition.SaveFile)
                    .Where(character => !char.IsControl(character))
                    .ToArray())
            .Replace("%", "%%", StringComparison.Ordinal);
        if (description.Length == 0)
        {
            description = "amz-monitor";
        }

        var builder = new StringBuilder();
        builder.AppendLine("[Unit]");
        builder.AppendLine($"Description=AMZ CLI Monitor ({description})");
        builder.AppendLine("After=network-online.target");
        builder.AppendLine("Wants=network-online.target");
        builder.AppendLine("StartLimitIntervalSec=300");
        builder.AppendLine("StartLimitBurst=3");
        builder.AppendLine();
        builder.AppendLine("[Service]");
        builder.AppendLine("Type=simple");
        builder.AppendLine(
            $"WorkingDirectory={Quote(definition.WorkingDirectory)}");
        builder.AppendLine(
            $"ExecStart={string.Join(' ', command.Select(Quote))}");
        builder.AppendLine("Environment=AMZ_MONITOR_STARTUP=systemd-user");
        builder.AppendLine(
            $"Environment=AMZ_MONITOR_STARTUP_ID={definition.Id}");
        builder.AppendLine("Restart=on-failure");
        builder.AppendLine("RestartSec=30");
        builder.AppendLine("KillSignal=SIGTERM");
        builder.AppendLine("TimeoutStopSec=30");
        builder.AppendLine();
        builder.AppendLine("[Install]");
        builder.AppendLine("WantedBy=default.target");
        return builder.ToString();
    }

    public static string Quote(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        var needsQuotes = value.Any(character =>
            char.IsWhiteSpace(character)
            || character is '"' or '\'' or '\\' or '%' or '$'
                or ';' or '#' or '&' or '|' or '(' or ')' or '<' or '>'
                or '*');
        if (!needsQuotes)
        {
            return value;
        }

        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("%", "%%", StringComparison.Ordinal)
            .Replace("$", "$$", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }

    private string UnitPath(StartupDefinition definition) =>
        Path.Combine(UnitDirectory, UnitName(definition));

    private string UnitName(StartupDefinition definition) =>
        string.IsNullOrWhiteSpace(definition.Name)
            ? NameFor(definition.Id)
            : definition.Name;

    private StartupProcessResult Run(params string[] arguments)
    {
        var args = new List<string> { "--user" };
        args.AddRange(arguments);
        return _runner.Run("systemctl", args);
    }

    private static void WriteAtomic(string path, string content)
    {
        var temporary = $"{path}.tmp-{Guid.NewGuid():N}";
        File.WriteAllText(
            temporary,
            content,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, path, overwrite: true);
    }
}
