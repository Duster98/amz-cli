namespace AmazonScraper.Update;

public enum UpdateStatus
{
    NoRelease,
    UpToDate,
    UpdateAvailable,
    ManualRequired,
}

public sealed record UpdateCheckResult(
    UpdateStatus Status,
    string CurrentVersion,
    string Platform,
    string Channel,
    ReleaseEnvelope? Envelope,
    ReleaseManifest? Manifest,
    string Message)
{
    public bool CanApply => Status == UpdateStatus.UpdateAvailable;

    public string? LatestVersion => Manifest?.Version;
}

public static class UpdateDecision
{
    public static UpdateCheckResult Evaluate(
        string currentVersion,
        string platform,
        string channel,
        ReleaseEnvelope? envelope,
        IReadOnlyDictionary<string, string>? publicKeys = null)
    {
        if (!SemanticVersion.TryParse(currentVersion, out var current))
        {
            throw new UpdateException($"Invalid current version: '{currentVersion}'.");
        }

        if (envelope is null)
        {
            return new UpdateCheckResult(
                UpdateStatus.NoRelease,
                currentVersion,
                platform,
                channel,
                null,
                null,
                "No release is published for this platform and channel.");
        }

        if (!string.Equals(envelope.Platform, platform, StringComparison.Ordinal)
            || !string.Equals(envelope.Channel, channel, StringComparison.Ordinal))
        {
            throw new UpdateException(
                "The received release does not match the requested platform or channel.");
        }

        ReleaseSignatureVerifier.VerifyOrThrow(
            envelope.Manifest,
            envelope.Signature,
            envelope.SigningKeyId,
            publicKeys);

        var manifest = ReleaseManifest.Parse(envelope.Manifest);
        if (!string.Equals(manifest.Version, envelope.Version, StringComparison.Ordinal)
            || !string.Equals(manifest.Platform, envelope.Platform, StringComparison.Ordinal)
            || !string.Equals(manifest.Channel, envelope.Channel, StringComparison.Ordinal))
        {
            throw new UpdateException(
                "The signed manifest does not match the release metadata.");
        }

        var latest = manifest.ParsedVersion;
        if (latest.IsPrerelease && channel == "stable")
        {
            throw new UpdateException(
                "The stable channel cannot serve a prerelease version.");
        }

        if (latest.CompareTo(current) <= 0)
        {
            return new UpdateCheckResult(
                UpdateStatus.UpToDate,
                currentVersion,
                platform,
                channel,
                envelope,
                manifest,
                $"Version {currentVersion} is already up to date.");
        }

        var minimum = manifest.ParsedMinSupportedVersion;
        if (minimum is not null && current.CompareTo(minimum) < 0)
        {
            return new UpdateCheckResult(
                UpdateStatus.ManualRequired,
                currentVersion,
                platform,
                channel,
                envelope,
                manifest,
                $"Updating to {manifest.Version} requires at least version " +
                $"{minimum}. Install the intermediate release manually.");
        }

        return new UpdateCheckResult(
            UpdateStatus.UpdateAvailable,
            currentVersion,
            platform,
            channel,
            envelope,
            manifest,
            $"Update available: {currentVersion} → {manifest.Version}.");
    }
}
