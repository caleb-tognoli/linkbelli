using System.Text;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Linkbelli.Infrastructure.Tests;

/// <summary>
/// Who can reach the job dashboard.
/// </summary>
/// <remarks>
/// The dashboard can trigger, delete and inspect every background job, arguments included. The
/// documentation says that outside development it is closed entirely unless credentials are set;
/// until these, nothing checked that it was.
/// </remarks>
public class HangfireDashboardAuthFilterTests
{
    private static (DashboardContext Context, HttpContext Http) Request(string? authorization = null)
    {
        // The dashboard context looks up optional services on the request; none are needed here.
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        if (authorization is not null)
        {
            http.Request.Headers.Authorization = authorization;
        }

        return (new AspNetCoreDashboardContext(new NoStorage(), new DashboardOptions(), http), http);
    }

    private static string Basic(string username, string password) =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));

    [Fact]
    public void Development_is_open()
    {
        var (context, _) = Request();

        Assert.True(new HangfireDashboardAuthFilter(allowAll: true, null, null).Authorize(context));
    }

    /// <summary>The claim the README makes, and the one that matters most.</summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("admin", null)]
    [InlineData(null, "secret")]
    [InlineData("admin", "")]
    public void With_no_credentials_configured_it_is_closed_whatever_is_sent(string? username, string? password)
    {
        var filter = new HangfireDashboardAuthFilter(allowAll: false, username, password);

        // Including the credentials an empty configuration would "match".
        foreach (var header in new[] { null, Basic("", ""), Basic("admin", ""), Basic("admin", "secret") })
        {
            var (context, http) = Request(header);
            Assert.False(filter.Authorize(context));
            Assert.Equal(401, http.Response.StatusCode);
        }
    }

    [Fact]
    public void The_right_credentials_let_you_in()
    {
        var (context, _) = Request(Basic("admin", "correct horse"));

        Assert.True(new HangfireDashboardAuthFilter(false, "admin", "correct horse").Authorize(context));
    }

    [Theory]
    [InlineData("admin", "wrong")]
    [InlineData("Admin", "correct horse")] // usernames are compared exactly
    [InlineData("admin", "correct horse ")]
    [InlineData("", "correct horse")]
    public void The_wrong_credentials_do_not(string username, string password)
    {
        var (context, http) = Request(Basic(username, password));

        Assert.False(new HangfireDashboardAuthFilter(false, "admin", "correct horse").Authorize(context));
        Assert.Equal(401, http.Response.StatusCode);
    }

    /// <summary>A browser only offers a login prompt when it is asked for one.</summary>
    [Fact]
    public void A_refusal_asks_for_basic_credentials()
    {
        var (context, http) = Request();

        new HangfireDashboardAuthFilter(false, "admin", "secret").Authorize(context);

        Assert.StartsWith("Basic realm=", http.Response.Headers.WWWAuthenticate.ToString());
    }

    [Theory]
    [InlineData("Bearer abc")]
    [InlineData("Basic not-base64!!")]
    [InlineData("Basic ")]
    [InlineData("Basic YWRtaW4=")] // "admin", no colon at all
    [InlineData("Basic OnNlY3JldA==")] // ":secret", an empty username
    public void Something_that_is_not_a_basic_login_is_refused_rather_than_thrown_on(string header)
    {
        var (context, http) = Request(header);

        Assert.False(new HangfireDashboardAuthFilter(false, "admin", "secret").Authorize(context));
        Assert.Equal(401, http.Response.StatusCode);
    }

    /// <summary>Only the first colon separates: a password may contain them.</summary>
    [Fact]
    public void A_password_with_a_colon_in_it_works()
    {
        var (context, _) = Request(Basic("admin", "pa:ss:word"));

        Assert.True(new HangfireDashboardAuthFilter(false, "admin", "pa:ss:word").Authorize(context));
    }

    /// <summary>The filter never touches storage; this only has to exist.</summary>
    private sealed class NoStorage : JobStorage
    {
        public override IMonitoringApi GetMonitoringApi() => throw new NotSupportedException();

        public override IStorageConnection GetConnection() => throw new NotSupportedException();
    }
}
