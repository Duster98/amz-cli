using System.Security.Cryptography;
using System.Text;

namespace AmazonScraper.Update;

public static class FileHash
{
    public static async Task<string> Sha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    public static string Sha256(byte[] data) =>
        Convert.ToHexStringLower(SHA256.HashData(data));

    public static string Sha256(string text) =>
        Sha256(Encoding.UTF8.GetBytes(text));

    public static bool LooksLikeSha256(string? value) =>
        value is { Length: 64 } && value.All(character =>
            (character >= '0' && character <= '9')
            || (character >= 'a' && character <= 'f'));

    public static bool FilesMatch(string path, long size, string sha256)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != size)
            {
                return false;
            }

            return string.Equals(
                Sha256Async(path).GetAwaiter().GetResult(),
                sha256,
                StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
