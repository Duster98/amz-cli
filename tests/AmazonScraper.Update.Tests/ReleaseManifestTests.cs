using System.Text;
using System.Text.Json.Nodes;
using AmazonScraper.Update;

namespace AmazonScraper.Update.Tests;

public class ReleaseManifestTests
{
    [Fact]
    public void Parses_a_valid_signed_manifest()
    {
        var (json, _, _) = TestRelease.Create();

        var manifest = ReleaseManifest.Parse(json);

        Assert.Equal("2.0.0", manifest.Version);
        Assert.Equal("linux-x64", manifest.Platform);
        Assert.Single(manifest.Files);
        Assert.Equal("amz-cli", manifest.Files[0].Path);
    }


    [Fact]
    public void Parses_a_schema_2_package_manifest()
    {
        var (json, _, _, _) = TestRelease.CreatePackage();

        var manifest = ReleaseManifest.Parse(json);

        Assert.Equal(2, manifest.Schema);
        Assert.True(manifest.IsPackageManifest);
        Assert.NotNull(manifest.Package);
        Assert.Equal("amz-cli-2.0.0-linux-x64.zip", manifest.Package!.Name);
        Assert.Equal("none", manifest.Files[0].Compression);
    }

    [Fact]
    public void Rejects_schema_2_without_package()
    {
        var (json, _, _, _) = TestRelease.CreatePackage();
        var node = JsonNode.Parse(json)!.AsObject();
        node.Remove("package");

        Assert.Throws<UpdateException>(
            () => ReleaseManifest.Parse(node.ToJsonString()));
    }


    [Fact]
    public void Accepts_a_legacy_schema_1_manifest_with_the_old_executable_name()
    {
        var (json, _, _) = TestRelease.Create();
        var legacy = json.Replace("\"path\":\"amz-cli\"", "\"path\":\"amazon\"", StringComparison.Ordinal)
            .Replace("stable/linux-x64/2.0.0/amz-cli.gz", "stable/linux-x64/2.0.0/amazon.gz", StringComparison.Ordinal);

        var manifest = ReleaseManifest.Parse(legacy);

        Assert.Equal(1, manifest.Schema);
        Assert.Equal("amazon", manifest.Files[0].Path);
    }

    [Fact]
    public void Parses_markdown_notes_and_plain_fallback()
    {
        var (json, _, _) = TestRelease.Create();
        var node = JsonNode.Parse(json)!.AsObject();
        node["notes"] = "Titolo\n- punto";
        node["notes_markdown"] = "## Titolo\n- **punto**";

        var manifest = ReleaseManifest.Parse(node.ToJsonString());

        Assert.Equal("Titolo\n- punto", manifest.Notes);
        Assert.Equal("## Titolo\n- **punto**", manifest.NotesMarkdown);
        Assert.Equal(manifest.NotesMarkdown, manifest.DisplayNotes);
        Assert.True(manifest.HasMarkdownNotes);
    }

    [Fact]
    public void Old_manifest_without_markdown_notes_uses_plain_fallback()
    {
        var (json, _, _) = TestRelease.Create();

        var manifest = ReleaseManifest.Parse(json);

        Assert.Null(manifest.NotesMarkdown);
        Assert.Equal("test release", manifest.DisplayNotes);
        Assert.False(manifest.HasMarkdownNotes);
    }

    [Fact]
    public void Rejects_markdown_notes_without_plain_fallback()
    {
        var (json, _, _) = TestRelease.Create();
        var node = JsonNode.Parse(json)!.AsObject();
        node["notes"] = "";
        node["notes_markdown"] = "## Titolo";

        Assert.Throws<UpdateException>(
            () => ReleaseManifest.Parse(node.ToJsonString()));
    }

    [Fact]
    public void Rejects_oversized_markdown_notes()
    {
        var (json, _, _) = TestRelease.Create();
        var node = JsonNode.Parse(json)!.AsObject();
        node["notes_markdown"] = new string('x', 20_001);

        Assert.Throws<UpdateException>(
            () => ReleaseManifest.Parse(node.ToJsonString()));
    }

    [Fact]
    public void Rejects_path_traversal()
    {
        var (json, _, _) = TestRelease.Create();
        var broken = json.Replace("\"path\":\"amz-cli\"", "\"path\":\"../amz-cli\"", StringComparison.Ordinal);

        Assert.Throws<UpdateException>(() => ReleaseManifest.Parse(broken));
    }

    [Fact]
    public void Rejects_missing_executable()
    {
        var (json, _, _) = TestRelease.Create();
        var broken = json.Replace("\"path\":\"amz-cli\"", "\"path\":\"other\"", StringComparison.Ordinal);

        Assert.Throws<UpdateException>(() => ReleaseManifest.Parse(broken));
    }

    [Fact]
    public void Rejects_download_path_outside_release_prefix()
    {
        var (json, _, _) = TestRelease.Create();
        var broken = json.Replace(
            "\"download_path\":\"stable/linux-x64/2.0.0/amz-cli.gz\"",
            "\"download_path\":\"stable/linux-x64/9.9.9/amz-cli.gz\"",
            StringComparison.Ordinal);

        Assert.Throws<UpdateException>(() => ReleaseManifest.Parse(broken));
    }

    [Fact]
    public void Rejects_invalid_hash()
    {
        var (json, _, _) = TestRelease.Create();
        var broken = json.Replace(
            "\"sha256\":\"",
            "\"sha256\":\"x",
            StringComparison.Ordinal);

        Assert.Throws<UpdateException>(() => ReleaseManifest.Parse(broken));
    }

    [Fact]
    public void Signature_rejects_a_tampered_manifest()
    {
        var (json, signature, publicKey) = TestRelease.Create();
        var keys = TestRelease.Keys(publicKey);

        ReleaseSignatureVerifier.VerifyOrThrow(json, signature, TestRelease.KeyId, keys);

        Assert.Throws<UpdateException>(() =>
            ReleaseSignatureVerifier.VerifyOrThrow(
                json + " ",
                signature,
                TestRelease.KeyId,
                keys));
    }

    [Fact]
    public void Signature_rejects_an_unknown_key()
    {
        var (json, signature, _) = TestRelease.Create();
        var keys = TestRelease.Keys("not-the-key");

        Assert.Throws<UpdateException>(() =>
            ReleaseSignatureVerifier.VerifyOrThrow(
                json,
                signature,
                "other-key",
                keys));
    }

    [Fact]
    public void Signature_rejects_invalid_base64()
    {
        var (json, _, publicKey) = TestRelease.Create();
        var keys = TestRelease.Keys(publicKey);

        Assert.Throws<UpdateException>(() =>
            ReleaseSignatureVerifier.VerifyOrThrow(
                json,
                "not base64!",
                TestRelease.KeyId,
                keys));
    }
}
