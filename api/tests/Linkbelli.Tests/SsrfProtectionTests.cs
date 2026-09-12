using System.Net;
using Linkbelli.Application.Http;

namespace Linkbelli.Tests;

public class SsrfProtectionTests
{
    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")]      // example.com
    [InlineData("2606:2800:220:1:248:1893:25c8:1946")] // public IPv6
    public void Allows_public_addresses(string ip)
    {
        Assert.True(SsrfProtection.IsPublic(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("127.0.0.1")]          // loopback
    [InlineData("10.0.0.5")]           // 10/8
    [InlineData("172.16.3.4")]         // 172.16/12
    [InlineData("172.31.255.1")]       // 172.16/12 upper
    [InlineData("192.168.1.1")]        // 192.168/16
    [InlineData("169.254.169.254")]    // link-local (cloud metadata!)
    [InlineData("100.64.0.1")]         // CGNAT
    [InlineData("0.0.0.0")]            // this-network
    [InlineData("224.0.0.1")]          // multicast
    [InlineData("::1")]                // IPv6 loopback
    [InlineData("fe80::1")]            // IPv6 link-local
    [InlineData("fc00::1")]            // IPv6 unique-local
    [InlineData("::ffff:10.0.0.1")]    // IPv4-mapped private
    public void Blocks_private_and_special_addresses(string ip)
    {
        Assert.False(SsrfProtection.IsPublic(IPAddress.Parse(ip)));
    }

    /// <summary>
    /// IPv6 forms that carry an IPv4 address inside them.
    /// </summary>
    /// <remarks>
    /// These all used to pass. None is v4-mapped, so the IPv4 rules never saw them, and none is
    /// link-local or unique-local, so the IPv6 rules had nothing to say either. They only matter
    /// on a host with 6to4 or NAT64 configured, which is unusual — but the address is attacker
    /// supplied and the check is cheap.
    /// </remarks>
    [Theory]
    [InlineData("2002:7f00:0001::")]   // 6to4 wrapping 127.0.0.1
    [InlineData("2002:a00:5::")]       // 6to4 wrapping 10.0.0.5
    [InlineData("2002:a9fe:a9fe::")]   // 6to4 wrapping 169.254.169.254, the metadata address
    [InlineData("64:ff9b::7f00:1")]    // NAT64 wrapping 127.0.0.1
    [InlineData("64:ff9b::a9fe:a9fe")] // NAT64 wrapping the metadata address
    [InlineData("::127.0.0.1")]        // deprecated IPv4-compatible loopback
    [InlineData("::169.254.169.254")]  // deprecated IPv4-compatible metadata
    [InlineData("100::1")]             // discard-only
    [InlineData("2001:db8::1")]        // documentation
    public void Blocks_ipv6_that_wraps_somewhere_private(string ip)
    {
        Assert.False(SsrfProtection.IsPublic(IPAddress.Parse(ip)));
    }

    /// <summary>The same wrappers around a genuinely public address stay reachable.</summary>
    [Theory]
    [InlineData("2002:808:808::")]     // 6to4 wrapping 8.8.8.8
    [InlineData("64:ff9b::808:808")]   // NAT64 wrapping 8.8.8.8
    public void Allows_ipv6_that_wraps_somewhere_public(string ip)
    {
        Assert.True(SsrfProtection.IsPublic(IPAddress.Parse(ip)));
    }
}
