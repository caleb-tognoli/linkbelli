using Linkbelli.Api.Auth;
using Linkbelli.Api.Common;
using Linkbelli.Api.Endpoints;
using Linkbelli.Api.Observability;
using Linkbelli.Api.OpenApi;
using Linkbelli.Application;
using Linkbelli.Application.Auth;
using Linkbelli.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Scalar.AspNetCore;
using System.Net;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Nothing needs to know what serves this, and saying so only helps somebody scanning.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);

// Composition root: Infrastructure (persistence + Identity) and Application (use cases).
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication(builder.Configuration);

// Serialize enums as strings (e.g. visibility "Public") in requests, responses, and the OpenAPI schema.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AppExceptionHandler>();

// CORS for browser frontends. Origins come from config "Cors:AllowedOrigins" (empty = none).
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (corsOrigins.Length > 0)
    {
        policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    }
}));

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<SecuritySchemeDocumentTransformer>();
    options.AddOperationTransformer<SecurityRequirementOperationTransformer>();
});

// --- Authentication: Identity bearer (registered by AddInfrastructure) + API key ---
builder.Services
    .AddAuthentication()
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationDefaults.Scheme, _ => { });

// Default policy carries requirements only — no schemes. Each endpoint names the
// scheme(s) it accepts; if schemes lived here they'd be unioned into every
// endpoint's policy, defeating per-endpoint restrictions like "bearer only".
var authz = builder.Services.AddAuthorizationBuilder()
    .SetDefaultPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

// One policy per API-key scope. Bearer principals and unscoped keys pass; a scoped key must
// hold the named scope (enforced by ScopeAuthorizationHandler).
foreach (var scope in Scopes.All)
{
    authz.AddPolicy(Scopes.Policy(scope), policy => policy
        .RequireAuthenticatedUser()
        .AddRequirements(new ScopeRequirement(scope)));
}

builder.Services.AddSingleton<IAuthorizationHandler, ScopeAuthorizationHandler>();

// --- Rate limiting skeleton: token bucket partitioned by API key, else by IP ---
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Tell clients when to retry. Buckets replenish every minute, so advertise that.
    options.OnRejected = (context, _) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
        }
        else
        {
            context.HttpContext.Response.Headers.RetryAfter = "60";
        }

        return ValueTask.CompletedTask;
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        RateLimitPartition.GetTokenBucketLimiter(ResolvePartitionKey(http), _ => new TokenBucketRateLimiterOptions
        {
            // Per user session (see ResolvePartitionKey). A single page load fans out to several
            // API calls, so allow generous bursts; replenish steadily.
            TokenLimit = 300,
            TokensPerPeriod = 150,
            ReplenishmentPeriod = TimeSpan.FromSeconds(10),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));

    // Outbound-fetch endpoints (/sources/preview) and the ones that send mail, where the cost
    // of a request falls on somebody else — a provider's send quota, or a person's inbox.
    //
    // Configurable because every anonymous caller shares one partition (their IP), which is also
    // what an integration suite looks like: a dozen tests exercising a mail flow from one host
    // are indistinguishable from abuse. The shipped default stays low.
    var sensitivePerMinute = builder.Configuration.GetValue<int?>("RateLimits:SensitivePerMinute") ?? 10;

    options.AddPolicy("sensitive", http =>
        RateLimitPartition.GetTokenBucketLimiter(ResolvePartitionKey(http), _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = sensitivePerMinute,
            TokensPerPeriod = sensitivePerMinute,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));

    // Signing in and signing up. Lockout already caps failures per account; this caps attempts
    // per caller, which is the half that stops one address working through a list of accounts —
    // and stops one address creating nine hundred of them a minute, which the global bucket
    // alone allowed. Generous enough that a person mistyping a password, or a household signing
    // up together, never meets it.
    var credentialsPerMinute = builder.Configuration.GetValue<int?>("RateLimits:CredentialsPerMinute") ?? 20;

    options.AddPolicy("credentials", http =>
        RateLimitPartition.GetTokenBucketLimiter(ResolvePartitionKey(http), _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = credentialsPerMinute,
            TokensPerPeriod = credentialsPerMinute,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));

    // Thumbnails are their own thing, and were wrongly on "sensitive". A playlist page asks for
    // one per row — dozens at once — so a ten-a-minute bucket meant the first ten loaded and
    // every other row silently lost its picture. A cache hit is a file read; only a miss costs an
    // outbound fetch, and that is bounded inside ThumbnailCache where the cost actually is.
    options.AddPolicy("thumbnails", http =>
        RateLimitPartition.GetTokenBucketLimiter(ResolvePartitionKey(http), _ => new TokenBucketRateLimiterOptions
        {
            // Sized for a long page plus scrolling through a grid, not for one screenful.
            TokenLimit = 400,
            TokensPerPeriod = 200,
            ReplenishmentPeriod = TimeSpan.FromSeconds(10),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));
});

// The API always sits behind something — the web BFF in every deployment, and usually a reverse
// proxy in front of that. Without this every request appears to come from the proxy's address,
// which is what made the anonymous rate-limit partition one bucket for the whole internet.
//
// Networks are configured rather than left at the default, which trusts nothing, and rather than
// KnownNetworks.Clear() + KnownProxies.Clear(), which trusts anybody who sends the header — the
// two ways to get this wrong. "ForwardedHeaders:TrustedNetworks" takes CIDRs; the default covers
// the private ranges a container network actually uses.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // One hop for the BFF, one for a proxy in front of it.
    options.ForwardLimit = builder.Configuration.GetValue<int?>("ForwardedHeaders:Limit") ?? 2;
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();

    var trusted = builder.Configuration.GetSection("ForwardedHeaders:TrustedNetworks").Get<string[]>()
        ?? ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8", "::1/128"];

    foreach (var cidr in trusted)
    {
        if (System.Net.IPNetwork.TryParse(cidr, out var network))
        {
            options.KnownIPNetworks.Add(network);
        }
    }
});

// Metrics always; tracing only when somewhere was configured to send it.
builder.Services.AddAppTelemetry(builder.Configuration);

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:MigrateAtStartup"))
{
    await app.MigrateDatabaseAsync();
}

// Before everything: the rate limiter, the logs and any endpoint that reads a client address
// should all see the caller rather than the proxy.
app.UseForwardedHeaders();

app.UseExceptionHandler();

app.MapAppMetrics();

// Ahead of routing on purpose: parameter binding consumes the body before any endpoint filter
// runs, so a request that wants its body hashed has to be made re-readable first.
app.Use(IdempotencyFilter.EnableBuffering);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options
        .WithTitle("Linkbelli API")
        .WithTheme(ScalarTheme.Default));
}
else
{
    // Tokens/keys are bearer credentials — never serve them over cleartext in production.
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseLinkbelliDashboard(); // Hangfire dashboard at /hangfire (dev only)

app.UseCors();
// Authentication first, so the limiter can partition on who is calling rather than on the
// credential they happened to present. See ResolvePartitionKey.
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Ok(new { name = "Linkbelli API", version = "v1" }));

// All business endpoints are versioned under /api/v1. Infra routes (/, /health, /openapi,
// /scalar, /hangfire) stay unversioned.
var v1 = app.MapGroup(ApiRoutes.V1);

// Opt-in by header: a POST carrying an Idempotency-Key can be retried safely, and one without
// behaves exactly as it always did.
v1.AddEndpointFilter<IdempotencyFilter>();

// Conditional GETs. Everything that polls this API re-downloaded an identical payload every
// time it looked.
v1.AddEndpointFilter<ETagFilter>();
v1.MapAuthEndpoints();
v1.MapMeEndpoints();
v1.MapApiKeyEndpoints();
v1.MapPlaylistEndpoints();
v1.MapPlaylistItemEndpoints();
v1.MapFolderEndpoints();
v1.MapLinkEndpoints();
v1.MapSourceEndpoints();
v1.MapTagEndpoints();
v1.MapAdminEndpoints();
v1.MapImportEndpoints();
v1.MapTrashEndpoints();
v1.MapExportEndpoints();
v1.MapBackupEndpoints();
v1.MapNotificationEndpoints();
v1.MapSearchEndpoints();
v1.MapAutomationEndpoints();
v1.MapFollowEndpoints();
v1.MapDuplicateEndpoints();
v1.MapSyncEndpoints();
v1.MapPublicPlaylistEndpoints();

app.Run();

// Partition per caller so they don't share a bucket. Critically, the web BFF proxies every request
// from one server IP, so partitioning by IP alone would lump all users together.
//
// Keyed on the authenticated user id, which is why UseRateLimiter runs after UseAuthentication.
// It used to hash the bearer token instead: refreshing a token minted a brand-new bucket, and
// every browser session a person had open got its own allowance — fine as a burst guard, useless
// as anything resembling a per-user limit.
static string ResolvePartitionKey(HttpContext http)
{
    var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!string.IsNullOrEmpty(userId))
    {
        return $"user:{userId}";
    }

    // API keys don't reach UseAuthentication (only the default scheme runs there; the key scheme
    // is named per-endpoint and resolves during authorization, after this). Their public id is a
    // stable, non-rotating identifier, so keying on it gives the same guarantee.
    if (http.Request.Headers.TryGetValue(ApiKeyToken.HeaderName, out var header)
        && ApiKeyToken.TryParse(header.ToString(), out var publicId, out _))
    {
        return $"key:{publicId}";
    }

    return $"ip:{http.Connection.RemoteIpAddress}";
}

public partial class Program; // exposed for WebApplicationFactory in integration tests
