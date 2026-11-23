using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;

namespace AmazonScraper.Core.Http;

/// <summary>
/// HTTP handler backed by the curl-impersonate executable. Used by all
/// non-Windows marketplaces. The class name is kept for compatibility with the
/// original UK-only implementation.
/// </summary>
internal sealed class UkCurlHandler(UkCookieJar jar, AmazonOptions options) : HttpMessageHandler
{
    private static readonly string[] ExecutableNames = OperatingSystem.IsWindows()
        ? ["curl-impersonate.exe"]
        : ["curl-impersonate"];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var executable = ResolveExecutable();
        var timeout = Math.Max(1, options.TimeoutSeconds);
        var headerFile = Path.GetTempFileName();
        var bodyFile = Path.GetTempFileName();
        var payloadFile = Path.GetTempFileName();

        try
        {
            jar.Apply(request);

            var arguments = new List<string>
            {
                "--silent",
                "--show-error",
                "--compressed",
                "--location",
                "--max-redirs", "10",
                "--max-time", timeout.ToString("0.###", CultureInfo.InvariantCulture),
                "--impersonate", ImpersonationProfile(options.BrowserProfile),
                "--dump-header", headerFile,
                "--output", bodyFile,
                "--write-out", "%{http_code}",
            };

            if (!string.IsNullOrWhiteSpace(options.UserAgent))
            {
                arguments.Add("--user-agent");
                arguments.Add(options.UserAgent);
            }

            var method = request.Method.Method;
            if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                arguments.Add("--request");
                arguments.Add(method);
            }

            if (request.Content is not null)
            {
                var payload = await request.Content.ReadAsByteArrayAsync(cancellationToken)
                    .ConfigureAwait(false);
                await File.WriteAllBytesAsync(payloadFile, payload, cancellationToken)
                    .ConfigureAwait(false);
                arguments.Add("--data-binary");
                arguments.Add($"@{payloadFile}");
            }

            AddHeaders(arguments, request.Headers);

            if (request.Content is not null)
            {
                AddHeaders(arguments, request.Content.Headers);
            }

            arguments.Add(request.RequestUri!.AbsoluteUri);

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
            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            if (!process.Start())
            {
                throw new HttpRequestException(
                $"Unable to start curl-impersonate: {executable}");
            }

            using var cancellation = cancellationToken.Register(() =>
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                    // Best effort.
                }
            });

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                    // Best effort.
                }
                throw;
            }

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new HttpRequestException(
                    $"curl-impersonate returned {process.ExitCode}: {stderr.Trim()}");
            }

            if (!int.TryParse(stdout.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var statusCode))
            {
                throw new HttpRequestException(
                    $"curl-impersonate did not return a valid status: {stdout.Trim()}");
            }

            var headers = ParseFinalHeaders(headerFile);
            var body = await File.ReadAllBytesAsync(bodyFile, cancellationToken)
                .ConfigureAwait(false);

            var response = new HttpResponseMessage((HttpStatusCode)statusCode)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(body),
            };

            foreach (var header in headers)
            {
                if (header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                {
                    response.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    continue;
                }
                if (IsContentHeader(header.Key))
                {
                    response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
                else if (!IsIgnoredHeader(header.Key))
                {
                    response.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            jar.Capture(response);
            return response;
        }
        finally
        {
            TryDelete(headerFile);
            TryDelete(bodyFile);
            TryDelete(payloadFile);
        }
    }

    private static void AddHeaders(
        List<string> arguments,
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        foreach (var header in headers)
        {
            if (ShouldSkip(header.Key))
            {
                continue;
            }

            // HttpClient splits comma-separated values such as
            // "text/html,*/*" into multiple collection entries. Emitting
            // them as separate headers makes Amazon reject the GLOW POST
            // with HTTP 406; combine them back into one browser-like line.
            var values = header.Value.ToArray();
            arguments.Add("--header");
            arguments.Add(
                values.Length == 1
                    ? $"{header.Key}: {values[0]}"
                    : $"{header.Key}: {string.Join(",", values)}");
        }
    }

    private static string ImpersonationProfile(CurlImpersonate.Enums.BrowserProfile profile)
    {
        var name = profile.ToString();
        return name
            .Replace("Android", "_android", StringComparison.Ordinal)
            .Replace("Ios", "_ios", StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    internal static string ResolveExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("AMZ_CURL_IMPERSONATE")
            ?? Environment.GetEnvironmentVariable("AMZ_UK_CURL");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (File.Exists(configured))
            {
                return configured;
            }
            throw new HttpRequestException(
                $"AMZ_CURL_IMPERSONATE points to a missing file: {configured}");
        }

        foreach (var name in ExecutableNames)
        {
            var bundled = Path.Combine(AppContext.BaseDirectory, "curl-impersonate", name);
            if (File.Exists(bundled))
            {
                return bundled;
            }

            var local = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(local))
            {
                return local;
            }

            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new HttpRequestException(
            "Non-Windows domains require an up-to-date 'curl-impersonate' command " +
            "(in PATH or set via AMZ_CURL_IMPERSONATE). " +
            "On Arch Linux: pacman -S curl-impersonate. " +
            "AMZ_UK_CURL is accepted as a legacy alias.");
    }

    private static bool ShouldSkip(string name) =>
        name.Equals("Host", StringComparison.OrdinalIgnoreCase)
        || name.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Connection", StringComparison.OrdinalIgnoreCase)
        || name.Equals("TE", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase);

    private static bool IsContentHeader(string name) =>
        name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase);

    private static bool IsIgnoredHeader(string name) =>
        name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Connection", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, List<string>> ParseFinalHeaders(string path)
    {
        var lines = File.ReadAllLines(path);
        var lastBlockStart = -1;
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase))
            {
                lastBlockStart = index;
            }
        }

        var headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (lastBlockStart < 0)
        {
            return headers;
        }

        for (var index = lastBlockStart + 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (!headers.TryGetValue(name, out var values))
            {
                values = [];
                headers[name] = values;
            }
            values.Add(value);
        }

        return headers;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Best effort.
        }
    }
}
