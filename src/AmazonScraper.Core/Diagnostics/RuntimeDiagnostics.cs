using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using AmazonScraper.Core.Http;
using CurlImpersonate;

namespace AmazonScraper.Core.Diagnostics;

public sealed record CurlRuntimeFile(
    string Name,
    string Path,
    bool Exists,
    long Size);

public sealed record SelfTestResult(
    string AppVersion,
    string OperatingSystem,
    string RuntimeIdentifier,
    string DotNetVersion,
    string CurlVersion,
    string CurlRuntimeIdentifier,
    string Transport,
    bool CurlAvailable,
    string CurlError,
    string CurlMode,
    IReadOnlyList<CurlRuntimeFile> CurlFiles);

public static class RuntimeDiagnostics
{
    private static readonly string[] WindowsBundledFiles =
    [
        "libcurl-impersonate.dll",
        "curl_shim.dll",
        "zlib.dll",
    ];

    public static SelfTestResult Run(string appVersion)
    {
        var curlVersion = "unavailable";
        var curlRuntime = "unavailable";
        var transport = "HttpClient";
        var curlAvailable = false;
        var curlError = string.Empty;
        var curlMode = "unavailable";
        var curlFiles = new List<CurlRuntimeFile>();

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                curlMode = "system";
                var executable = UkCurlHandler.ResolveExecutable();
                curlVersion = ReadCurlVersion(executable);
                curlRuntime = executable;
                transport = "curl-impersonate (system)";
            }
            else
            {
                curlMode = "bundled";
                curlVersion = CurlGlobal.Version;
                curlRuntime = CurlGlobal.NativeRuntimeIdentifier;
                transport = "curl-impersonate";
                foreach (var name in WindowsBundledFiles)
                {
                    var path = Path.Combine(AppContext.BaseDirectory, name);
                    var info = new FileInfo(path);
                    curlFiles.Add(new CurlRuntimeFile(
                        name,
                        path,
                        info.Exists,
                        info.Exists ? info.Length : 0));
                }

                var missing = curlFiles
                    .Where(file => !file.Exists)
                    .Select(file => file.Name)
                    .ToList();
                if (missing.Count > 0)
                {
                    curlError =
                        "Missing bundled curl-impersonate files: " +
                        string.Join(", ", missing);
                }
            }

            curlAvailable = true;
        }
        catch (Exception ex)
        {
            curlError = ex.Message;
        }

        return new SelfTestResult(
            appVersion,
            RuntimeInformation.OSDescription,
            RuntimeInformation.RuntimeIdentifier,
            Environment.Version.ToString(),
            curlVersion,
            curlRuntime,
            transport,
            curlAvailable,
            curlError,
            curlMode,
            curlFiles);
    }

    private static string ReadCurlVersion(string executable)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("--version");
        if (!process.Start())
        {
            throw new InvalidOperationException(
                $"Unable to start curl-impersonate: {executable}");
        }

        var output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(5000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"curl-impersonate did not respond: {executable}");
        }

        var match = Regex.Match(output, @"libcurl/([^\s]+)");
        if (match.Success)
        {
            return $"libcurl/{match.Groups[1].Value}";
        }

        return output.Split('\n').FirstOrDefault()?.Trim()
            ?? "unknown version";
    }
}
