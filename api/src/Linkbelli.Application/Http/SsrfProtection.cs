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
    public static ValueTask<Stream> ConnectCallback(SocketsHttpConnectionContext context, CancellationToken ct) =>
        ConnectAsync(context, IsPublic, ct);

    /// <summary>
    /// The guard for webhook deliveries, which an operator may widen to their own network.
    /// </summary>
    /// <remarks>
    /// The events people most want to send go to things on a home network — a Home Assistant box
    /// at 192.168.1.10 is the canonical webhook receiver — and the public-only rule refuses every
    /// one of them. So an operator, and only an operator, can allow the private ranges. Never
    /// loopback, which is this server and whatever else listens on it, and never link-local,
    /// which is where a cloud instance's metadata service answers with its credentials.
    /// </remarks>
    public static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> ConnectCallbackFor(
        bool allowPrivateNetworks) =>
        allowPrivateNetworks
            ? (context, ct) => ConnectAsync(context, a => IsPublic(a) || IsPrivateNetwork(a), ct)
            : ConnectCallback;

    /// <summary>
    /// The ranges a home or office network is numbered from: RFC 1918 and IPv6 unique-local.
    /// Nothing else that <see cref="IsPublic"/> refuses.
    /// </summary>
    public static bool IsPrivateNetwork(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        Span<byte> b = stackalloc byte[16];
        if (!address.TryWriteBytes(b, out var written))
        {
            return false;
        }

        return written switch
        {
            4 => b[0] == 10
                || (b[0] == 172 && b[1] is >= 16 and <= 31)
                || (b[0] == 192 && b[1] == 168),
            16 => (b[0] & 0xFE) == 0xFC,
            _ => false,
        };
    }

    private static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context, Func<IPAddress, bool> permitted, CancellationToken ct)
    {
        var endpoint = context.DnsEndPoint;
        var addresses = await Dns.GetHostAddressesAsync(endpoint.Host, ct).ConfigureAwait(false);
        var allowed = addresses.Where(permitted).ToArray();
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

        // 2002::/16 — 6to4 carries an IPv4 address in the next four bytes, so 2002:7f00:1:: is
        // loopback written in IPv6. The v4 checks never see it because it is not v4-mapped.
        if (b[0] == 0x20 && b[1] == 0x02)
        {
            return IsPublicV4(new IPAddress(b.Slice(2, 4).ToArray()));
        }

        // 64:ff9b::/96 — NAT64 does the same thing for a translation gateway.
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B)
        {
            return IsPublicV4(new IPAddress(b.Slice(12, 4).ToArray()));
        }

        // ::a.b.c.d — the deprecated IPv4-compatible form. IsIPv4MappedToIPv6 is false for it
        // (that wants the ::ffff: prefix), so ::127.0.0.1 would otherwise pass as public.
        if (IsAllZero(b[..12]))
        {
            return IsPublicV4(new IPAddress(b.Slice(12, 4).ToArray()));
        }

        // 100::/64 discard-only, and 2001:db8::/32 documentation.
        if (b[0] == 0x01 && b[1] == 0x00 && IsAllZero(b.Slice(2, 6))) return false;
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8) return false;

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
