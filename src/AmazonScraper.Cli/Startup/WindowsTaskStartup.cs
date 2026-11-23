using System.Security;
using System.Text;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Startup;

public sealed class WindowsTaskStartup : IStartupBackend
{
    public const string KindName = "windows-task";

    private readonly IStartupProcessRunner _runner;
    private readonly string? _helperPath;

    public WindowsTaskStartup(
        IStartupProcessRunner? runner = null,
        string? helperPath = null)
    {
        _runner = runner ?? new StartupProcessRunner();
        _helperPath = helperPath;
    }

    public string Kind => KindName;

    public string HelperPath =>
        _helperPath
        ?? FindHelper()
        ?? Path.Combine(AppContext.BaseDirectory, "amz-notify.exe");

    public string NameFor(string id) => $"AmzCLI-{id}";

    public bool IsAvailable(out string? error)
    {
        if (!OperatingSystem.IsWindows())
        {
            error = "Task Scheduler is only available on Windows.";
            return false;
        }

        if (!File.Exists(HelperPath))
        {
            error =
                "amz-notify.exe not found next to amz-cli.exe: " +
                HelperPath;
            return false;
        }

        var amazonExecutable = Path.Combine(
            AppContext.BaseDirectory,
            "amz-cli.exe");
        if (!File.Exists(amazonExecutable))
        {
            error =
                "amz-cli.exe not found next to the helper: " +
                amazonExecutable;
            return false;
        }

        var schtasks = SchtasksPath();
        if (!File.Exists(schtasks))
        {
            error = "schtasks.exe not found: " + schtasks;
            return false;
        }

        error = null;
        return true;
    }

    public void Install(StartupDefinition definition)
    {
        var xml = BuildTaskXml(
            definition,
            HelperPath,
            CurrentUserIdentity());
        var temporary = Path.Combine(
            Path.GetTempPath(),
            $"amz-cli-startup-{Guid.NewGuid():N}.xml");
        File.WriteAllText(temporary, xml, Encoding.Unicode);
        try
        {
            var result = Run(
                ["/Create", "/TN", TaskName(definition), "/XML", temporary, "/F"]);
            if (!result.Success)
            {
                throw new MonitorException(
                    $"Task creation failed: {result.Detail()}");
            }
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception)
            {
                // Best effort.
            }
        }
    }

    public void Start(StartupDefinition definition)
    {
        var result = Run(["/Run", "/TN", TaskName(definition)]);
        if (!result.Success)
        {
            throw new MonitorException(
                $"Task start failed: {result.Detail()}");
        }
    }

    public void Stop(StartupDefinition definition)
    {
        TryStopMonitor(definition);
        _ = Run(["/End", "/TN", TaskName(definition)]);
    }

    public bool Remove(StartupDefinition definition)
    {
        var name = TaskName(definition);
        var ended = false;
        var deleted = false;
        try
        {
            Stop(definition);
            ended = true;
        }
        catch (MonitorException)
        {
            // The task may not be running or Task Scheduler may be unavailable.
        }

        try
        {
            deleted = Run(["/Delete", "/TN", name, "/F"]).Success;
        }
        catch (MonitorException)
        {
            // The startup definition can still be removed.
        }

        return ended || deleted;
    }

    public bool Exists(StartupDefinition definition)
    {
        var result = Run(
            ["/Query", "/TN", TaskName(definition), "/FO", "LIST"]);
        return result.Success;
    }

    public static string BuildTaskXml(
        StartupDefinition definition,
        string helperPath,
        string userId)
    {
        var name = string.IsNullOrWhiteSpace(definition.Name)
            ? $"AmzCLI-{definition.Id}"
            : definition.Name;
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
        builder.AppendLine(
            "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">");
        builder.AppendLine("  <RegistrationInfo>");
        builder.AppendLine(
            "    <Description>AMZ CLI Monitor auto-start</Description>");
        builder.AppendLine("  </RegistrationInfo>");
        builder.AppendLine("  <Triggers>");
        builder.AppendLine("    <LogonTrigger>");
        builder.AppendLine("      <Enabled>true</Enabled>");
        builder.AppendLine(
            $"      <UserId>{Xml(userId)}</UserId>");
        builder.AppendLine("      <Delay>PT10S</Delay>");
        builder.AppendLine("    </LogonTrigger>");
        builder.AppendLine("  </Triggers>");
        builder.AppendLine("  <Principals>");
        builder.AppendLine("    <Principal id=\"Author\">");
        builder.AppendLine(
            $"      <UserId>{Xml(userId)}</UserId>");
        builder.AppendLine("      <LogonType>InteractiveToken</LogonType>");
        builder.AppendLine("      <RunLevel>LeastPrivilege</RunLevel>");
        builder.AppendLine("    </Principal>");
        builder.AppendLine("  </Principals>");
        builder.AppendLine("  <Settings>");
        builder.AppendLine(
            "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>");
        builder.AppendLine(
            "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
        builder.AppendLine(
            "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>");
        builder.AppendLine("    <AllowHardTerminate>true</AllowHardTerminate>");
        builder.AppendLine("    <StartWhenAvailable>true</StartWhenAvailable>");
        builder.AppendLine(
            "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>");
        builder.AppendLine("    <IdleSettings>");
        builder.AppendLine("      <StopOnIdleEnd>false</StopOnIdleEnd>");
        builder.AppendLine("      <RestartOnIdle>false</RestartOnIdle>");
        builder.AppendLine("    </IdleSettings>");
        builder.AppendLine("    <AllowStartOnDemand>true</AllowStartOnDemand>");
        builder.AppendLine("    <Enabled>true</Enabled>");
        builder.AppendLine("    <Hidden>true</Hidden>");
        builder.AppendLine("    <RunOnlyIfIdle>false</RunOnlyIfIdle>");
        builder.AppendLine("    <WakeToRun>false</WakeToRun>");
        builder.AppendLine("    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>");
        builder.AppendLine("    <Priority>7</Priority>");
        builder.AppendLine("  </Settings>");
        builder.AppendLine("  <Actions Context=\"Author\">");
        builder.AppendLine("    <Exec>");
        builder.AppendLine(
            $"      <Command>{Xml(helperPath)}</Command>");
        builder.AppendLine(
            $"      <Arguments>--startup-launch {Xml(definition.Id)}</Arguments>");
        builder.AppendLine(
            $"      <WorkingDirectory>{Xml(definition.WorkingDirectory)}</WorkingDirectory>");
        builder.AppendLine("    </Exec>");
        builder.AppendLine("  </Actions>");
        builder.AppendLine("</Task>");
        return builder.ToString();
    }

    public static string CurrentUserIdentity()
    {
        var domain = Environment.UserDomainName;
        return string.IsNullOrWhiteSpace(domain)
            ? Environment.UserName
            : $"{domain}\\{Environment.UserName}";
    }

    private static string? FindHelper()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "amz-notify.exe"),
            Path.Combine(AppContext.BaseDirectory, "notify", "amz-notify.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private void TryStopMonitor(StartupDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.SavePath)
            || string.IsNullOrWhiteSpace(definition.SaveFile))
        {
            return;
        }

        var directory = Path.GetDirectoryName(HelperPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = AppContext.BaseDirectory;
        }

        var executable = Path.Combine(directory, "amz-cli.exe");
        var arguments = new List<string>
        {
            "monitor",
            "--stop",
            "--save-path",
            definition.SavePath,
            "--save-file",
            definition.SaveFile,
        };

        try
        {
            _ = _runner.Run(
                executable,
                arguments,
                string.IsNullOrWhiteSpace(definition.WorkingDirectory)
                    ? null
                    : definition.WorkingDirectory,
                TimeSpan.FromSeconds(30));
        }
        catch (Exception)
        {
            // The monitor may already be gone; /End below is the fallback.
        }
    }

    private string TaskName(StartupDefinition definition) =>
        string.IsNullOrWhiteSpace(definition.Name)
            ? NameFor(definition.Id)
            : definition.Name;

    private static string SchtasksPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "schtasks.exe";
        }

        var system = Environment.SystemDirectory;
        return string.IsNullOrWhiteSpace(system)
            ? "schtasks.exe"
            : Path.Combine(system, "schtasks.exe");
    }

    private StartupProcessResult Run(IReadOnlyList<string> arguments) =>
        _runner.Run(SchtasksPath(), arguments);

    private static string Xml(string? value) =>
        SecurityElement.Escape(value) ?? string.Empty;
}
