using Linkbelli.Api.Auth;
using Linkbelli.Api.Common;
using Linkbelli.Api.Endpoints;
using Linkbelli.Api.OpenApi;
using Linkbelli.Application;
using Linkbelli.Application.Auth;
using Linkbelli.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Scalar.AspNetCore;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Composition root: Infrastructure (persistence + Identity) and Application (use cases).
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

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
            TokenLimit = 100,
            TokensPerPeriod = 100,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));

    // Reserved for outbound-fetch endpoints (e.g. /sources/preview) in later milestones.
    options.AddPolicy("sensitive", http =>
        RateLimitPartition.GetTokenBucketLimiter(ResolvePartitionKey(http), _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 10,
            TokensPerPeriod = 10,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));
});

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:MigrateAtStartup"))
{
    await app.MigrateDatabaseAsync();
}

app.UseExceptionHandler();

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
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Ok(new { name = "Linkbelli API", version = "v1" }));

// All business endpoints are versioned under /api/v1. Infra routes (/, /health, /openapi,
// /scalar, /hangfire) stay unversioned.
var v1 = app.MapGroup(ApiRoutes.V1);
v1.MapAuthEndpoints();
v1.MapMeEndpoints();
v1.MapApiKeyEndpoints();
v1.MapPlaylistEndpoints();
v1.MapPlaylistItemEndpoints();
v1.MapLinkEndpoints();
v1.MapSourceEndpoints();
v1.MapTagEndpoints();
v1.MapAdminEndpoints();
v1.MapPublicPlaylistEndpoints();

app.Run();

// Prefer the API key's public prefix as the partition; fall back to client IP.
static string ResolvePartitionKey(HttpContext http)
{
    if (http.Request.Headers.TryGetValue(ApiKeyToken.HeaderName, out var header)
        && ApiKeyToken.TryParse(header.ToString(), out var publicId, out _))
    {
        return $"key:{publicId}";
    }

    return $"ip:{http.Connection.RemoteIpAddress}";
}

public partial class Program; // exposed for WebApplicationFactory in integration tests
