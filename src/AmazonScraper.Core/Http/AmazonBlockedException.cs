namespace AmazonScraper.Core.Http;

public sealed class AmazonBlockedException : Exception
{
    public AmazonBlockedException(string detail = "")
        : base(BuildMessage(detail))
    {
    }

    private static string BuildMessage(string detail)
    {
        var message = "Amazon returned an anti-bot verification (Akamai/captcha) instead of the page";
        if (!string.IsNullOrEmpty(detail))
        {
            message += $" ({detail})";
        }

        return message + ". Try again in a few minutes.";
    }
}
