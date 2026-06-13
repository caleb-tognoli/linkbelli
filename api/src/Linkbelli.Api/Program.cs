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
builder.Services.AddAuthorizationBuilder()
    .SetDefaultPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

// --- Rate limiting skeleton: token bucket partitioned by API key, else by IP ---
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
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

app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapApiKeyEndpoints();
app.MapPlaylistEndpoints();
app.MapPlaylistItemEndpoints();
app.MapLinkEndpoints();
app.MapSourceEndpoints();
app.MapAdminEndpoints();

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
