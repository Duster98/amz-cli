namespace AmazonScraper.Cli.Diagnostics;

/// <summary>
/// Reads the PE subsystem without loading the executable. Windows GUI apps use
/// subsystem 2; console apps use 3. The notifier must stay GUI so Task Scheduler
/// does not flash a console window at logon.
/// </summary>
public static class PeSubsystem
{
    public static bool TryRead(string path, out ushort subsystem)
    {
        subsystem = 0;
        try
        {
            using var stream = File.OpenRead(path);
            var header = new byte[8192];
            var read = stream.Read(header, 0, header.Length);
            if (read < 64
                || header[0] != (byte)'M'
                || header[1] != (byte)'Z')
            {
                return false;
            }

            var pe = BitConverter.ToInt32(header, 0x3C);
            var optional = pe + 24;
            var subsystemOffset = optional + 68;
            if (pe < 0
                || subsystemOffset < 0
                || subsystemOffset + 2 > read)
            {
                return false;
            }

            if (header[pe] != (byte)'P'
                || header[pe + 1] != (byte)'E'
                || header[pe + 2] != 0
                || header[pe + 3] != 0)
            {
                return false;
            }

            subsystem = BitConverter.ToUInt16(header, subsystemOffset);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
