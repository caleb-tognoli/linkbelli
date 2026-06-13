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
}
