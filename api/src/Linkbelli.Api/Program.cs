using Linkbelli.Api.Auth;
using Linkbelli.Api.Endpoints;
using Linkbelli.Api.OpenApi;
using Linkbelli.Core.Auth;
using Linkbelli.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<LinkbelliDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<LinkbelliDbContext>("database");

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<SecuritySchemeDocumentTransformer>();
    options.AddOperationTransformer<SecurityRequirementOperationTransformer>();
});

// --- Authentication: Identity bearer (interactive) + API key (programmatic) ---
builder.Services
    .AddAuthentication()
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationDefaults.Scheme, _ => { });

builder.Services.AddIdentityApiEndpoints<ApplicationUser>()
    .AddEntityFrameworkStores<LinkbelliDbContext>();

// Usernames are always unique in Identity; require emails to be unique too so
// login-by-email resolves to a single account.
builder.Services.Configure<IdentityOptions>(options => options.User.RequireUniqueEmail = true);

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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Interactive API reference UI at /scalar, served from the OpenAPI document.
    app.MapScalarApiReference(options => options
        .WithTitle("Linkbelli API")
        .WithTheme(ScalarTheme.Default));
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Ok(new { name = "Linkbelli API", version = "v1" }));

app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapApiKeyEndpoints();

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
