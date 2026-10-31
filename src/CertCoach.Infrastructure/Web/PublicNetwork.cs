using System.Net;
using System.Net.Sockets;

namespace CertCoach.Infrastructure.Web;

/// <summary>
/// Keeps outline imports to the public internet. The check runs when the connection is made, after DNS
/// resolution and on every redirect, so neither a hostname pointing at 127.0.0.1 nor a redirect to the cloud
/// metadata endpoint gets through.
/// </summary>
internal static class PublicNetwork
{
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        // A proxy would make the connection, and the check, on our behalf.
        UseProxy = false,
        ConnectCallback = ConnectAsync,
    };

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, ct);
        var allowed = addresses.Where(IsPublic).ToArray();
        if (allowed.Length == 0)
        {
            throw new PrivateAddressException(host);
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(b[0] is 0 or 10 or 127 or >= 224 // "this" network, private, loopback, multicast and reserved
                || (b[0] == 100 && (b[1] & 0xC0) == 64) // carrier-grade NAT
                || (b[0] == 169 && b[1] == 254) // link-local, including cloud metadata
                || (b[0] == 172 && (b[1] & 0xF0) == 16)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)
                || (b[0] == 198 && (b[1] & 0xFE) == 18)); // benchmarking
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return false;
        }

        if (address.Equals(IPAddress.IPv6Any) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
            || (b[0] & 0xFE) == 0xFC // unique local
            || (b[0] == 0x20 && b[1] == 0x02)) // 6to4, which can wrap a private IPv4 address
        {
            return false;
        }

        // NAT64 (64:ff9b::/96) carries an IPv4 address in its last four bytes.
        var nat64 = b[0] == 0 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && b[4..12].All(x => x == 0);
        return !nat64 || IsPublic(new IPAddress(b[12..]));
    }
}

internal sealed class PrivateAddressException(string host)
    : Exception($"{host} is on a private network.");
