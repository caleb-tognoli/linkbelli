using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace Linkbelli.Application.Http;

/// <summary>
/// SSRF protection for outbound fetches of user-supplied URLs. Used as a
/// SocketsHttpHandler.ConnectCallback so validation happens at the actual connect
/// (closing the DNS-rebinding TOCTOU gap): we only ever open a socket to an IP we
/// have classified as public, on every request and every redirect hop.
/// </summary>
public static class SsrfProtection
{
    public static async ValueTask<Stream> ConnectCallback(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var endpoint = context.DnsEndPoint;
        var addresses = await Dns.GetHostAddressesAsync(endpoint.Host, ct).ConfigureAwait(false);
        var allowed = addresses.Where(IsPublic).ToArray();
        if (allowed.Length == 0)
        {
            throw new HttpRequestException($"Refusing to connect to non-public host '{endpoint.Host}'.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, endpoint.Port, ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>True only for globally-routable addresses; rejects loopback/private/link-local/etc.</summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPublicV4(address),
            AddressFamily.InterNetworkV6 => IsPublicV6(address),
            _ => false,
        };
    }

    private static bool IsPublicV4(IPAddress address)
    {
        Span<byte> b = stackalloc byte[4];
        address.TryWriteBytes(b, out _);

        if (b[0] is 0 or 10 or 127) return false;                       // this-network, 10/8, loopback
        if (b[0] == 100 && b[1] is >= 64 and <= 127) return false;       // 100.64/10 CGNAT
        if (b[0] == 169 && b[1] == 254) return false;                    // 169.254/16 link-local
        if (b[0] == 172 && b[1] is >= 16 and <= 31) return false;        // 172.16/12
        if (b[0] == 192 && b[1] == 168) return false;                    // 192.168/16
        if (b[0] == 192 && b[1] == 0 && b[2] == 0) return false;         // 192.0.0/24
        if (b[0] == 198 && b[1] is 18 or 19) return false;               // 198.18/15 benchmark
        if (b[0] >= 224) return false;                                   // multicast/reserved/broadcast
        return true;
    }

    private static bool IsPublicV6(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return false;                 // ::1
        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast) return false;

        Span<byte> b = stackalloc byte[16];
        address.TryWriteBytes(b, out _);
        if (IsAllZero(b)) return false;                                  // ::
        if ((b[0] & 0xFE) == 0xFC) return false;                         // fc00::/7 unique-local
        return true;
    }

    private static bool IsAllZero(ReadOnlySpan<byte> bytes)
    {
        foreach (var x in bytes)
        {
            if (x != 0) return false;
        }

        return true;
    }
}
