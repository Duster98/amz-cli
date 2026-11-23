using System.Text;

namespace AmazonScraper.Core.Monitoring;

/// <summary>
/// Append-only text file used by JSONL and CSV outputs. It repairs a partial
/// trailing line left by a crash and validates the CSV header when appending.
/// </summary>
internal sealed class MonitorFileWriter : IAsyncDisposable
{
    private readonly StreamWriter _writer;
    private bool _disposed;

    private MonitorFileWriter(StreamWriter writer)
    {
        _writer = writer;
    }

    public static MonitorFileWriter Open(string path, string? csvHeader)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        RepairTrailingLine(path);
        ValidateCsvHeader(path, csvHeader);

        var stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read);
        var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            NewLine = "\n",
            AutoFlush = false,
        };

        if (csvHeader is not null && stream.Length == 0)
        {
            writer.WriteLine(csvHeader);
            writer.Flush();
            stream.Flush();
        }

        return new MonitorFileWriter(writer);
    }

    public async Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        await _writer.WriteLineAsync(line.AsMemory(), cancellationToken)
            .ConfigureAwait(false);
        await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _writer.FlushAsync().ConfigureAwait(false);
        await _writer.DisposeAsync().ConfigureAwait(false);
    }

    private static void ValidateCsvHeader(string path, string? csvHeader)
    {
        if (csvHeader is null || !File.Exists(path))
        {
            return;
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        if (stream.Length == 0)
        {
            return;
        }

        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: true);
        var firstLine = reader.ReadLine();
        if (!string.Equals(firstLine, csvHeader, StringComparison.Ordinal))
        {
            throw new MonitorException(
                $"Incompatible CSV header in '{path}'. " +
                "Use a new --save-file or remove the file.");
        }
    }

    private static void RepairTrailingLine(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.Read);
        if (stream.Length == 0)
        {
            return;
        }

        stream.Seek(-1, SeekOrigin.End);
        if (stream.ReadByte() == '\n')
        {
            return;
        }

        var position = stream.Length - 1;
        while (position >= 0)
        {
            stream.Seek(position, SeekOrigin.Begin);
            if (stream.ReadByte() == '\n')
            {
                break;
            }

            position--;
        }

        stream.SetLength(position + 1);
    }
}
