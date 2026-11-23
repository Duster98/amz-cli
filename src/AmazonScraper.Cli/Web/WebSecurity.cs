using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace AmazonScraper.Cli.Web;

public static class WebSecurity
{
    public const string CookieName = "amz_cli_web_session";
    public const string CsrfHeader = "X-Amz-Cli-Web";
    public const string ControlHeader = "X-Amz-Cli-Token";
    public const string ControlPath = "/api/v1/control";

    public static string CreateSecret(int bytes = 32)
    {
        var buffer = RandomNumberGenerator.GetBytes(bytes);
        return Convert.ToBase64String(buffer)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool ValuesEqual(string? left, string? right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
        {
            return false;
        }

        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    public static IReadOnlySet<string> LoopbackHosts { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "127.0.0.1",
            "localhost",
            "::1",
        };

    public static bool IsAllowedHost(HostString host) =>
        IsAllowedHost(host, LoopbackHosts);

    public static bool IsAllowedHost(
        HostString host,
        IReadOnlySet<string> allowedHosts) =>
        allowedHosts.Contains(WebNetwork.NormalizeHost(host.Host));

    public static bool IsAllowedOrigin(
        string? origin,
        int serverPort,
        bool development) =>
        IsAllowedOrigin(origin, serverPort, development, LoopbackHosts);

    public static bool IsAllowedOrigin(
        string? origin,
        int serverPort,
        bool development,
        IReadOnlySet<string> allowedHosts)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            // Non-browser callers do not send Origin. Mutations still require
            // the custom CSRF header and an authenticated session cookie.
            return true;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
            || !IsAllowedHost(new HostString(uri.Host), allowedHosts))
        {
            return false;
        }

        if (uri.Port == serverPort)
        {
            return true;
        }

        return development && uri.Port == 5173;
    }

    public static CookieOptions SessionCookie() => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        IsEssential = true,
    };

    public static void ApplyHeaders(
        HttpResponse response,
        bool development,
        string? requestHost = null)
    {
        var connect = development
            ? "'self' http://localhost:5173 http://127.0.0.1:5173 " +
              "ws://localhost:5173 ws://127.0.0.1:5173"
            : "'self' ws://127.0.0.1:* ws://localhost:*";
        if (!string.IsNullOrWhiteSpace(requestHost))
        {
            connect += $" ws://{requestHost.Trim()}";
        }
        response.Headers["Content-Security-Policy"] = string.Join(
            "; ",
            "default-src 'self'",
            "script-src 'self'",
            "style-src 'self' 'unsafe-inline'",
            "img-src 'self' data: https://*.media-amazon.com https://images-na.ssl-images-amazon.com https://images-eu.ssl-images-amazon.com",
            $"connect-src {connect}",
            "font-src 'self' data:",
            "manifest-src 'self'",
            "object-src 'none'",
            "base-uri 'none'",
            "form-action 'self'",
            "frame-ancestors 'none'");
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["X-Frame-Options"] = "DENY";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
        response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
        response.Headers["Permissions-Policy"] =
            "camera=(), microphone=(), geolocation=()";
    }
}
