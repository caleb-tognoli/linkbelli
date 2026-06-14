using System.Text;
using Hangfire.Dashboard;

namespace Linkbelli.Infrastructure;

/// <summary>
/// Authorizes the Hangfire dashboard. In Development it's open; otherwise it requires HTTP Basic
/// credentials matching config (closed by default if none are configured). The API itself is
/// token-based with no interactive session, so Basic auth is the pragmatic gate for the dashboard.
/// </summary>
public sealed class HangfireDashboardAuthFilter(bool allowAll, string? username, string? password)
    : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        if (allowAll)
        {
            return true;
        }

        var http = context.GetHttpContext();
        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password) && IsValidBasic(http.Request.Headers.Authorization.ToString()))
        {
            return true;
        }

        http.Response.StatusCode = 401;
        http.Response.Headers["WWW-Authenticate"] = "Basic realm=\"Linkbelli Hangfire\"";
        return false;
    }

    private bool IsValidBasic(string header)
    {
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..].Trim()));
            var separator = decoded.IndexOf(':');
            return separator > 0
                && decoded[..separator] == username
                && decoded[(separator + 1)..] == password;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
