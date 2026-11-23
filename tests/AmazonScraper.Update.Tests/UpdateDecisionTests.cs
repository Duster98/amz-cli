using AmazonScraper.Update;

namespace AmazonScraper.Update.Tests;

public class UpdateDecisionTests
{
    [Fact]
    public void Returns_no_release_when_rpc_is_empty()
    {
        var result = UpdateDecision.Evaluate("1.0.0", "linux-x64", "stable", null);

        Assert.Equal(UpdateStatus.NoRelease, result.Status);
        Assert.False(result.CanApply);
    }

    [Fact]
    public void Returns_update_available_for_a_newer_signed_release()
    {
        var (manifest, signature, publicKey) = TestRelease.Create("2.0.0");
        var envelope = TestRelease.Envelope(manifest, signature);

        var result = UpdateDecision.Evaluate(
            "1.0.0",
            "linux-x64",
            "stable",
            envelope,
            TestRelease.Keys(publicKey));

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
        Assert.True(result.CanApply);
        Assert.Equal("2.0.0", result.LatestVersion);
    }

    [Fact]
    public void Returns_up_to_date_for_the_same_or_older_release()
    {
        var (manifest, signature, publicKey) = TestRelease.Create("2.0.0");
        var envelope = TestRelease.Envelope(manifest, signature);

        var result = UpdateDecision.Evaluate(
            "2.0.0",
            "linux-x64",
            "stable",
            envelope,
            TestRelease.Keys(publicKey));

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
    }

    [Fact]
    public void Requires_manual_update_below_min_supported_version()
    {
        var (manifest, signature, publicKey) = TestRelease.Create(
            "2.0.0",
            minSupported: "1.5.0");
        var envelope = TestRelease.Envelope(manifest, signature);

        var result = UpdateDecision.Evaluate(
            "1.0.0",
            "linux-x64",
            "stable",
            envelope,
            TestRelease.Keys(publicKey));

        Assert.Equal(UpdateStatus.ManualRequired, result.Status);
        Assert.False(result.CanApply);
    }

    [Fact]
    public void Stable_channel_rejects_prerelease()
    {
        var (manifest, signature, publicKey) = TestRelease.Create(
            "2.0.0-beta.1",
            minSupported: "1.0.0");
        var envelope = TestRelease.Envelope(
            manifest,
            signature,
            version: "2.0.0-beta.1");

        Assert.Throws<UpdateException>(() =>
            UpdateDecision.Evaluate(
                "1.0.0",
                "linux-x64",
                "stable",
                envelope,
                TestRelease.Keys(publicKey)));
    }

    [Fact]
    public void Rejects_a_platform_mismatch()
    {
        var (manifest, signature, publicKey) = TestRelease.Create("2.0.0");
        var envelope = TestRelease.Envelope(
            manifest,
            signature,
            platform: "win-x64");

        Assert.Throws<UpdateException>(() =>
            UpdateDecision.Evaluate(
                "1.0.0",
                "linux-x64",
                "stable",
                envelope,
                TestRelease.Keys(publicKey)));
    }
}
