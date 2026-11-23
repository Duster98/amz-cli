using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace AmazonScraper.Cli.Web;

/// <summary>
/// Local network addresses and host names used by remote mode. Only hosts
/// that belong to this machine are accepted, so an arbitrary Host header
/// cannot be used for DNS rebinding.
/// </summary>
public static class WebNetwork
{
    private static readonly Lazy<IReadOnlyList<NetworkAddress>> CachedAddresses =
        new(CollectAddresses);

    public static IReadOnlyList<string> LocalAddresses() =>
        CachedAddresses.Value
            .Select(item => item.Address.ToString())
            .ToArray();

    public static string? FindLanAddress()
    {
        var addresses = CachedAddresses.Value;
        var preferred = addresses.FirstOrDefault(item => item.Score > 0);
        return (preferred ?? addresses.FirstOrDefault())?.Address.ToString();
    }

    public static IReadOnlySet<string> AllowedHosts()
    {
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "127.0.0.1",
            "localhost",
            "::1",
        };

        foreach (var address in LocalAddresses())
        {
            hosts.Add(NormalizeHost(address));
        }

        try
        {
            var name = Dns.GetHostName();
            if (!string.IsNullOrWhiteSpace(name))
            {
                hosts.Add(NormalizeHost(name));
                hosts.Add(NormalizeHost(name + ".local"));
            }
        }
        catch (Exception)
        {
            // The host name is optional; the interface addresses are enough.
        }

        return hosts;
    }

    public static string NormalizeHost(string host)
    {
        var value = host.Trim();
        if (value.StartsWith('[') && value.EndsWith(']'))
        {
            value = value[1..^1];
        }
        return value.TrimEnd('.').ToLowerInvariant();
    }

    public static string FormatHostForUrl(string address)
    {
        if (IPAddress.TryParse(address, out var ip)
            && ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return $"[{address}]";
        }
        return address;
    }

    private static IReadOnlyList<NetworkAddress> CollectAddresses()
    {
        var results = new List<NetworkAddress>();
        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up
                    || networkInterface.NetworkInterfaceType
                        == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var unicast in networkInterface
                             .GetIPProperties()
                             .UnicastAddresses)
                {
                    var address = unicast.Address;
                    if (IPAddress.IsLoopback(address))
                    {
                        continue;
                    }
                    if (address.AddressFamily != AddressFamily.InterNetwork
                        && address.AddressFamily != AddressFamily.InterNetworkV6)
                    {
                        continue;
                    }
                    if (address.IsIPv6LinkLocal)
                    {
                        continue;
                    }
                    if (address.AddressFamily == AddressFamily.InterNetwork
                        && address.ToString().StartsWith(
                            "169.254.",
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    results.Add(new NetworkAddress(
                        address,
                        Score(networkInterface, address)));
                }
            }
        }
        catch (Exception)
        {
            // Restricted environments may not expose network interfaces.
        }

        return results
            .GroupBy(
                item => item.Address.ToString(),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.Score)
                .First())
            .OrderByDescending(item => item.Score)
            .ToArray();
    }

    private static int Score(NetworkInterface networkInterface, IPAddress address)
    {
        var score = 0;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            score += 100;
        }
        if (IsPrivate(address))
        {
            score += 50;
        }
        if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Ethernet
            or NetworkInterfaceType.Wireless80211)
        {
            score += 40;
        }

        var name = networkInterface.Name.ToLowerInvariant();
        if (name.StartsWith("en", StringComparison.Ordinal)
            || name.StartsWith("eth", StringComparison.Ordinal)
            || name.StartsWith("wl", StringComparison.Ordinal)
            || name.StartsWith("wlan", StringComparison.Ordinal))
        {
            score += 20;
        }
        if (name.StartsWith("docker", StringComparison.Ordinal)
            || name.StartsWith("br-", StringComparison.Ordinal)
            || name.StartsWith("veth", StringComparison.Ordinal)
            || name.StartsWith("virbr", StringComparison.Ordinal)
            || name.StartsWith("vmnet", StringComparison.Ordinal)
            || name.StartsWith("tap", StringComparison.Ordinal))
        {
            score -= 200;
        }

        return score;
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127);
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6
            && address.IsIPv6UniqueLocal;
    }

    private sealed record NetworkAddress(IPAddress Address, int Score);
}
