namespace AmazonScraper.Update;

/// <summary>
/// Production endpoints and the public material shipped with the CLI. The RSA
/// public key can only verify, never create, a release signature.
/// </summary>
public static class UpdateEndpoints
{
    public const string GitHubRepository = "Duster98/amz-cli";

    public const string ReleasesBaseUrl =
        "https://github.com/Duster98/amz-cli/releases";

    public const string LatestFeedBaseUrl = ReleasesBaseUrl + "/latest/download";

    public const string InstallerBaseUrl =
        ReleasesBaseUrl + "/download/installer-stable";

    public const string SigningKeyId = "release-2026-01";

    public const long MaxDownloadSize = 50L * 1024 * 1024;

    public const long MaxPackageDownloadSize = 256L * 1024 * 1024;

    public const int MaxManifestBytes = 1024 * 1024;

    public const int MaxFiles = 128;

    public static string FeedUrl(string platform, string channel) =>
        channel == "beta"
            ? $"{ReleasesBaseUrl}/download/beta-feed/{platform}.json"
            : $"{LatestFeedBaseUrl}/{platform}.json";

    public static bool IsAllowedHost(string host) =>
        host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("githubusercontent.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);

    public static readonly IReadOnlyDictionary<string, string> PublicKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SigningKeyId] = PublicKeyPem,
        };

    public const string PublicKeyPem =
        """
        -----BEGIN PUBLIC KEY-----
        MIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEApkzs+7aP+j8pjFfbH6vP
        4O2BIf6rS3yYVk3JxKdAkpDItK5q4/pnWhqd6Mfnfr6XTpdKQ1mORKaIF1+dYIah
        lEah2MO3JZMbUWaZmesVpQymkVmm1UmuoMaPne7KTo0OynI87B90rgEYbA7OHab2
        2fP54FIv9lLgw+ImUIlRN5TMj1InyUE5TG1ibYOtJgeEncVGATqzSSqL5/jJC6HD
        y16XhhDHzs2g/X+qBrFuPz9YmWxihUjHdJ8MTpLjIjzWGGwP+2PbJxp0kyOj/Bmh
        CRRQM3V77mSWgl4Vmwdvodhb4BCB07TKLuSI0X6qFq+oTO5CbgssfeyFVaKsGNGV
        jRU1CKu4/gwf/7AP1LltYlQVLMbDPKdTQUm2razkJVGpQt8eT5yOXncqeDrGwjLI
        RyvFoT5NcG9Nzibsl+n4lH4EWHPzl7Hf4bPiKca/dOYmFpBSj5M+FVLfv62sE1Iu
        4bEAbeS7AVHRlB2VDQD8bedCLc/nJ+QcNaJfw+0Fuqi7rT25p/VeDgKVmqhqXo40
        yoMI52V+me5I3aKPS/gIrA2aDAnbYShj/BKRw0FVOM23p5qCQqkFwpIExkcS5gPT
        xLL1A7Lnc2VhoOdtMpd/KIupuFIbFRH5yUhdrIM1MDNbaEaamcIdQm1xAAdexZfL
        f6U/Sxu3tIfusO1kb75Jkw0CAwEAAQ==
        -----END PUBLIC KEY-----
        """;
}
