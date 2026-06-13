using System.Net.Http.Json;

namespace Linkbelli.IntegrationTests;

/// <summary>Shared request helpers + lightweight response DTOs for the integration tests.</summary>
public static class ApiTestHelpers
{
    public const string Password = "Passw0rd!";

    public static string NewUsername() => "u" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>Registers a fresh user and returns a bearer access token.</summary>
    public static async Task<string> RegisterAndLoginAsync(this HttpClient client, string username)
    {
        var register = await client.PostAsJsonAsync("/api/v1/auth/register",
            new { username, email = $"{username}@example.com", password = Password });
        register.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { login = username, password = Password });
        login.EnsureSuccessStatusCode();

        var token = await login.Content.ReadFromJsonAsync<TokenDto>();
        return token!.AccessToken;
    }

    public record TokenDto(string TokenType, string AccessToken, int ExpiresIn, string RefreshToken);

    public record PlaylistDto(Guid Id, string Name, string Slug, string? Description, string Visibility, int ItemCount, string[] Tags);

    public record ItemDto(Guid Id, long Position);

    public record PagedDto<T>(List<T> Items, string? NextCursor);

    public record ApiKeyCreatedDto(Guid Id, string Name, string Prefix, string Token, string[] Scopes);

    public record PublicSummaryDto(string OwnerUsername, string Slug, string Name, string? Description, int ItemCount, string[] Tags);

    public record TagSummaryDto(string Name, int PlaylistCount);

    public record SourceDto(Guid Id, string Name, string Type, string Visibility, Guid[] PlaylistIds);

    public record SharedSourceDto(Guid Id, string Name, string Type, string OwnerUsername);

    public record LinkPreviewDto(string CanonicalUrl, string Host, string? Title, string? Description, string? ImageUrl, string? SiteName);
}
