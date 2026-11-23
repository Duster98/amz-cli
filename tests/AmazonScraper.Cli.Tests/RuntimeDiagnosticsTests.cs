using AmazonScraper.Core.Diagnostics;

namespace AmazonScraper.Cli.Tests;

public class RuntimeDiagnosticsTests
{
    [Fact]
    public void Runtime_diagnostics_reports_a_curl_mode()
    {
        var result = RuntimeDiagnostics.Run("3.2.0");

        Assert.Contains(
            result.CurlMode,
            new[] { "system", "bundled", "unavailable" });
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(3, result.CurlFiles.Count);
            Assert.Contains(
                result.CurlFiles,
                file => file.Name == "libcurl-impersonate.dll");
            Assert.Contains(
                result.CurlFiles,
                file => file.Name == "curl_shim.dll");
            Assert.Contains(
                result.CurlFiles,
                file => file.Name == "zlib.dll");
        }
        else
        {
            Assert.Empty(result.CurlFiles);
        }
    }
}
