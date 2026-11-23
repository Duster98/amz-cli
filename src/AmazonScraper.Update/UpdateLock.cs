using System.Text;

namespace AmazonScraper.Update;

/// <summary>
/// Advisory lock preventing two updater processes from writing the same
/// installation. The OS releases the handle automatically on crash.
/// </summary>
public sealed class UpdateLock : IDisposable
{
    private readonly FileStream _stream;
    private readonly string _path;
    private bool _disposed;

    private UpdateLock(string path, FileStream stream)
    {
        _path = path;
        _stream = stream;
    }

    public static UpdateLock Acquire(string? path = null)
    {
        path ??= UpdatePaths.LockPath();
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
            return new UpdateLock(path, stream);
        }
        catch (IOException)
        {
            throw new UpdateException(
                "Another update is already in progress (lock: " + path + ").");
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
