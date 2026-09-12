using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Linkbelli.Application.Common;

/// <summary>
/// Refuses to start when something required is missing, rather than starting and failing later.
/// </summary>
/// <remarks>
/// The motivating case is <c>DataProtection:KeyRingPath</c>. The README calls it required in
/// production and says exactly what happens without it — keys regenerate on restart, everybody is
/// signed out, and every encrypted source secret becomes permanently undecryptable — while the
/// code simply skipped persistence when it was absent. So a deployment that forgot the variable
/// booted cleanly, worked perfectly, and destroyed data on its first restart. Silent, delayed and
/// unrecoverable is the worst shape a configuration mistake can have.
///
/// Development is exempt throughout: a local run should need no configuration at all, which is
/// what makes `docker compose up` work with an empty tree.
/// </remarks>
public static class StartupConfiguration
{
    public static void ValidateOrThrow(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        var missing = new List<string>();

        Require("ConnectionStrings:Default", "the database to connect to");

        Require(
            "DataProtection:KeyRingPath",
            "a stable directory for the Data Protection key ring — without one, keys regenerate "
            + "on every restart, which signs out every user and makes encrypted source secrets "
            + "permanently undecryptable");

        // Reset and notification links are built from this and never from the request's Host
        // header, so an unset value does not fall back to something wrong — it produces mail
        // nobody can act on.
        if (!string.IsNullOrWhiteSpace(configuration["Email:Host"]))
        {
            Require("Email:PublicUrl", "where links inside outgoing mail should point");
            Require("Email:FromAddress", "who mail comes from");
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Linkbelli cannot start. Missing required configuration:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, missing.Select(m => "  - " + m))
                + Environment.NewLine
                + "Environment variables use __ for nesting, e.g. DataProtection__KeyRingPath. "
                + "See the README's production configuration table.");
        }

        void Require(string key, string why)
        {
            if (string.IsNullOrWhiteSpace(configuration[key]))
            {
                missing.Add($"{key} — {why}.");
            }
        }
    }
}
