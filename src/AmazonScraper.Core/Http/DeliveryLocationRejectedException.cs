namespace AmazonScraper.Core.Http;

/// <summary>
/// Raised when Amazon returns a location dialog without the fields needed to
/// submit an address change.
/// </summary>
public sealed class DeliveryLocationRejectedException(string message)
    : Exception(message);
