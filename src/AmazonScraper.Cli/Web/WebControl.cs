using System.Net.Http;

namespace AmazonScraper.Cli.Web;

public static class WebControl
{
    public static async Task<int> StatusAsync(CancellationToken cancellationToken)
    {
        var info = WebSessionStore.TryRead();
        if (info is null)
        {
            Console.WriteLine("No active web interface.");
            return 1;
        }

        if (!await PingAsync(info, cancellationToken).ConfigureAwait(false))
        {
            Console.WriteLine(
                $"Web session not running (PID {info.Pid}, port {info.Port}).");
            return 1;
        }

        Console.WriteLine($"Web interface running: http://127.0.0.1:{info.Port}/");
        if (!string.IsNullOrWhiteSpace(info.LanUrl))
        {
            Console.WriteLine($"LAN: {info.LanUrl}");
        }
        Console.WriteLine($"PID: {info.Pid}");
        Console.WriteLine($"Version: {info.Version}");
        return 0;
    }

    public static async Task<int> StopAsync(
        bool quiet,
        CancellationToken cancellationToken)
    {
        var info = WebSessionStore.TryRead();
        if (info is null)
        {
            if (!quiet)
            {
                Console.WriteLine("No active web interface.");
            }
            return 0;
        }

        if (!await PingAsync(info, cancellationToken).ConfigureAwait(false))
        {
            WebSessionStore.Delete();
            if (!quiet)
            {
                Console.WriteLine("Web session not running: removed.");
            }
            return 0;
        }

        if (!quiet)
        {
            Console.WriteLine("Stopping the web interface...");
        }

        try
        {
            using var client = CreateClient();
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"http://127.0.0.1:{info.Port}/api/v1/control/shutdown");
            request.Headers.Add(WebSecurity.ControlHeader, info.Token);
            using var response = await client
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine(
                    $"The interface refused to stop (HTTP {(int)response.StatusCode}).");
                return 1;
            }

            for (var attempt = 0; attempt < 50; attempt++)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                if (!await PingAsync(info, cancellationToken).ConfigureAwait(false))
                {
                    WebSessionStore.Delete();
                    if (!quiet)
                    {
                        Console.WriteLine("Web interface stopped.");
                    }
                    return 0;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not stop the web interface: {ex.Message}");
            return 1;
        }

        Console.Error.WriteLine(
            "The web interface did not stop within the expected time.");
        return 1;
    }

    public static async Task<bool> PingAsync(
        WebSessionInfo info,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient();
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"http://127.0.0.1:{info.Port}/api/v1/control/ping");
            request.Headers.Add(WebSecurity.ControlHeader, info.Token);
            using var response = await client
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static async Task<bool> TryOpenExistingAsync(
        bool open,
        CancellationToken cancellationToken)
    {
        var info = WebSessionStore.TryRead();
        if (info is null)
        {
            return false;
        }
        if (!await PingAsync(info, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var localUrl = string.IsNullOrWhiteSpace(info.LocalUrl)
            ? $"http://127.0.0.1:{info.Port}/#t={info.Token}"
            : info.LocalUrl;
        if (open && !WebBrowser.Open(localUrl))
        {
            Console.WriteLine($"Open manually: {localUrl}");
        }
        Console.WriteLine($"Web interface already running: http://127.0.0.1:{info.Port}/");
        if (!string.IsNullOrWhiteSpace(info.LanUrl))
        {
            Console.WriteLine($"LAN: {info.LanUrl}");
        }
        return true;
    }

    public static async Task<bool> HasIncompatibleInstanceAsync(
        bool remote,
        int port,
        CancellationToken cancellationToken)
    {
        var info = WebSessionStore.TryRead();
        if (info is null
            || !await PingAsync(info, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if (remote && !info.Remote)
        {
            Console.Error.WriteLine(
                "The web interface is already running in local-only mode. " +
                "Stop it with 'amz-cli web --stop' and start it again with --remote.");
            return true;
        }

        if (port > 0 && info.Port != port)
        {
            Console.Error.WriteLine(
                $"The web interface is already running on port {info.Port}. " +
                $"Stop it with 'amz-cli web --stop' before starting it on port {port}.");
            return true;
        }

        return false;
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            UseProxy = false,
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(3),
        };
    }
}
