using Linkbelli.Application.Auth;
using Linkbelli.Application.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Linkbelli.Api.Observability;

/// <summary>
/// Metrics and tracing. Observability here was one health check and Hangfire's dashboard: enough
/// to say whether the process was alive, and nothing about whether it was doing its job.
/// </summary>
public static class TelemetrySetup
{
    /// <summary>Where the scrape endpoint lives when it is switched on.</summary>
    public const string MetricsPath = "/metrics";

    public static IServiceCollection AddAppTelemetry(this IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection("Telemetry");
        var otlpEndpoint = section["Otlp:Endpoint"];
        var metricsEnabled = section.GetValue("Metrics:Enabled", true);

        var builder = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: section["ServiceName"] ?? "linkbelli-api",
                serviceVersion: typeof(TelemetrySetup).Assembly.GetName().Version?.ToString()));

        if (metricsEnabled)
        {
            builder.WithMetrics(metrics => metrics
                .AddMeter(AppMetrics.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter());
        }

        // Traces only go anywhere if somewhere was configured. Collecting spans and dropping them
        // on the floor costs the same as collecting spans that are read.
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            builder.WithTracing(tracing => tracing
                .AddSource(AppMetrics.ActivitySourceName)
                .AddAspNetCoreInstrumentation(options =>
                    // The scrape endpoint is hit constantly and says nothing; tracing it is noise.
                    options.Filter = context => context.Request.Path != MetricsPath)
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint)));
        }

        return services;
    }

    /// <summary>
    /// Maps the scrape endpoint.
    /// </summary>
    /// <remarks>
    /// Behind the Admin role by default. Metrics name every host this instance fetches and how
    /// much of everything there is; that is not a public fact about somebody's private
    /// collection. Set <c>Telemetry:Metrics:AllowAnonymous</c> when the port is only reachable
    /// from a scraper.
    /// </remarks>
    public static void MapAppMetrics(this WebApplication app)
    {
        var section = app.Configuration.GetSection("Telemetry");
        if (!section.GetValue("Metrics:Enabled", true))
        {
            return;
        }

        var endpoint = app.MapPrometheusScrapingEndpoint(MetricsPath);

        if (section.GetValue("Metrics:AllowAnonymous", false))
        {
            endpoint.AllowAnonymous();
        }
        else
        {
            endpoint.RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = IdentityConstants.BearerScheme,
                Roles = AppRoles.Admin,
            });
        }

        endpoint.ExcludeFromDescription();
    }
}
