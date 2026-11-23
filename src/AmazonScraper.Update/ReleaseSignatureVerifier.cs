using System.Security.Cryptography;
using System.Text;

namespace AmazonScraper.Update;

public static class ReleaseSignatureVerifier
{
    public static void VerifyOrThrow(
        string manifest,
        string signatureBase64,
        string signingKeyId,
        IReadOnlyDictionary<string, string>? publicKeys = null)
    {
        var keys = publicKeys ?? UpdateEndpoints.PublicKeys;
        if (!keys.TryGetValue(signingKeyId, out var publicKeyPem))
        {
            throw new UpdateException(
                $"Unknown signing key: '{signingKeyId}'.");
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(signatureBase64);
        }
        catch (FormatException ex)
        {
            throw new UpdateException("Invalid release signature (base64).", ex);
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            var valid = rsa.VerifyData(
                Encoding.UTF8.GetBytes(manifest),
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss);
            if (!valid)
            {
                throw new UpdateException("Invalid release signature.");
            }
        }
        catch (UpdateException)
        {
            throw;
        }
        catch (CryptographicException ex)
        {
            throw new UpdateException("Release signature verification failed.", ex);
        }
    }
}
