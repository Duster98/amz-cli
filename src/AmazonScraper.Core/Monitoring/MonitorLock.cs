using System.Text;

namespace AmazonScraper.Core.Monitoring;

/// <summary>
/// Prevents two monitor processes from writing the same data file. The lock
/// file is advisory and is deleted on graceful shutdown; a stale file left by
/// a crash is harmless because the OS releases the handle.
/// </summary>
public sealed class MonitorLock : IDisposable
{
    private readonly FileStream _stream;
    private readonly string _path;
    private bool _disposed;

    private MonitorLock(string path, FileStream stream)
    {
        _path = path;
        _stream = stream;
    }

    public static MonitorLock Acquire(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        try
        {
            var stream = new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            stream.SetLength(0);
            using (var writer = new StreamWriter(
                       stream,
                       new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                       bufferSize: 1024,
                       leaveOpen: true))
            {
                writer.Write(Environment.ProcessId);
                writer.Flush();
            }

            stream.Flush();
            return new MonitorLock(path, stream);
        }
        catch (IOException)
        {
            var pid = TryReadPid(path);
            throw new MonitorException(
                pid is null
                    ? $"Monitor already running (lock: {path})."
                    : $"Monitor already running (PID {pid}, lock: {path}).");
        }
    }

    private static string? TryReadPid(string path)
    {
        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stream.Dispose();
        try
        {
            File.Delete(_path);
        }
        catch (Exception)
        {
            // Best effort.
        }
    }
}
