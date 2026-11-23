using System.Text;

namespace AmazonScraper.Cli.Monitoring;

public sealed class MonitorLogger : IDisposable
{
    private readonly bool _quiet;
    private readonly bool _console;
    private readonly StreamWriter? _file;

    public MonitorLogger(bool quiet, string? logPath)
    {
        _quiet = quiet;
        var startup = Environment.GetEnvironmentVariable("AMZ_MONITOR_STARTUP");
        _console = !string.Equals(
                Environment.GetEnvironmentVariable("AMZ_MONITOR_DETACHED"),
                "1",
                StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(startup);
        if (!string.IsNullOrWhiteSpace(logPath))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(logPath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var stream = new FileStream(
                logPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read);
            _file = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
                NewLine = "\n",
            };
        }
    }

    public void Info(string message)
    {
        if (!_quiet)
        {
            Write("INFO", message, console: true);
        }
    }

    public void Warn(string message) =>
        Write("WARN", message, console: true);

    public void Error(string message) =>
        Write("ERROR", message, console: true);

    public void Alert(string message) =>
        Write("ALERT", message, console: true);

    public void Summary(string message) =>
        Write("SUMMARY", message, console: false);

    private void Write(string level, string message, bool console)
    {
        var line = $"{DateTimeOffset.UtcNow:yyyy-MM-dd'T'HH:mm:ss'Z'} [{level}] {message}";
        if (_console && console)
        {
            Console.Error.WriteLine(line);
        }

        _file?.WriteLine(line);
    }

    public void Dispose() => _file?.Dispose();
}
