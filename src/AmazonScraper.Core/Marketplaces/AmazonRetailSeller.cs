namespace AmazonScraper.Core.Marketplaces;

/// <summary>
/// Amazon's own retail accounts. AOD renders these offers as plain text
/// ("Venditore Amazon") without a seller-profile link, so the seller ID has to
/// be resolved from the marketplace instead of the page.
/// </summary>
public static class AmazonRetailSeller
{
    public static string IdFor(string marketplaceId) => marketplaceId switch
    {
        "it" => "A11IL2PNWYJU7H",
        "com" => "ATVPDKIKX0DER",
        "uk" => "A3P5ROKL5A1OLE",
        "de" => "A3JWKAKR8XB7XF",
        "fr" => "A1X6FK5RDHNB96",
        "es" => "A1AT7YVPFBWXBL",
        "jp" => "AN1VRQENFRJN5",
        _ => string.Empty,
    };

    public static bool IsName(string? sellerName)
    {
        if (string.IsNullOrWhiteSpace(sellerName))
        {
            return false;
        }

        var name = sellerName.Trim();
        return name.Equals("Amazon", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Amazon.", StringComparison.OrdinalIgnoreCase);
    }
}
