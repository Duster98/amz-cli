using AmazonScraper.Cli.Diagnostics;

namespace AmazonScraper.Cli.Tests;

public class PeSubsystemTests
{
    [Fact]
    public void Reads_a_gui_pe_subsystem()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Root, "fake.exe");
        var bytes = new byte[8192];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        var pe = 0x80;
        BitConverter.GetBytes(pe).CopyTo(bytes, 0x3C);
        bytes[pe] = (byte)'P';
        bytes[pe + 1] = (byte)'E';
        BitConverter.GetBytes((ushort)2).CopyTo(bytes, pe + 24 + 68);
        File.WriteAllBytes(path, bytes);

        var ok = PeSubsystem.TryRead(path, out var subsystem);

        Assert.True(ok);
        Assert.Equal((ushort)2, subsystem);
    }

    [Fact]
    public void Rejects_a_non_pe_file()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Root, "fake.exe");
        File.WriteAllText(path, "not a PE");

        Assert.False(PeSubsystem.TryRead(path, out _));
    }
}
