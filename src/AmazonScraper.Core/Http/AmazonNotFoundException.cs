namespace AmazonScraper.Core.Http;

/// <summary>
/// Amazon returned HTTP 404 for a product/page that does not exist in this
/// marketplace. A "Page Not Found" page can contain the
/// <c>api-services-support@amazon.com</c> marker used by soft-block pages, so
/// the status code must be checked before the HTML block markers.
/// </summary>
public sealed class AmazonNotFoundException : Exception
{
    public AmazonNotFoundException(string detail = "")
        : base(BuildMessage(detail))
    {
    }

    private static string BuildMessage(string detail)
    {
        var message = "Page not found on this marketplace (HTTP 404)";
        if (!string.IsNullOrWhiteSpace(detail))
        {
            message += $": {detail}";
        }

        return message + ".";
    }
}
