using System.Net;
using System.Net.Http.Json;
using Linkbelli.Application.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Accounts that predate the username rules.
/// </summary>
/// <remarks>
/// Usernames are now constrained, because they are public path segments and people had been
/// registering their email address as one. Everyone who did that before the rule still has to be
/// able to use their account — in particular to sign in and to reset a password, which is the
/// path that would quietly break if the rule were enforced on every write rather than at sign-up.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class LegacyUsernameTests(PostgresApiFactory factory)
{
    /// <summary>
    /// Creates a user whose name the current rules would reject, the way one already in the
    /// database got there — through the store, without going past the endpoint's validation.
    /// </summary>
    private async Task<ApplicationUser> SeedLegacyUserAsync(string username)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var store = scope.ServiceProvider.GetRequiredService<IUserStore<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName = username,
            Email = $"{Guid.NewGuid():N}@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        user.NormalizedUserName = users.NormalizeName(username);
        user.NormalizedEmail = users.NormalizeEmail(user.Email);
        user.PasswordHash = users.PasswordHasher.HashPassword(user, Password);
        user.SecurityStamp = Guid.NewGuid().ToString();

        await store.CreateAsync(user, CancellationToken.None);
        return user;
    }

    [Fact]
    public async Task Can_still_sign_in()
    {
        var user = await SeedLegacyUserAsync($"legacy{Guid.NewGuid():N}"[..14] + "@example.com");
        var client = factory.CreateClient();

        var res = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { login = user.UserName, password = Password });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    /// <summary>
    /// The regression this class exists for. Identity validates the whole user on update, so
    /// enforcing the character set through IdentityOptions would make every password reset for
    /// one of these accounts fail on the username — a field the person never touched.
    /// </summary>
    [Fact]
    public async Task Can_still_change_a_password()
    {
        var user = await SeedLegacyUserAsync($"legacy{Guid.NewGuid():N}"[..14] + "@example.com");

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var stored = await users.FindByNameAsync(user.UserName!);
        Assert.NotNull(stored);

        var token = await users.GeneratePasswordResetTokenAsync(stored);
        var result = await users.ResetPasswordAsync(stored, token, "An0therPassw0rd!");

        Assert.True(
            result.Succeeded,
            "reset failed: " + string.Join("; ", result.Errors.Select(e => $"{e.Code} {e.Description}")));
    }

    /// <summary>New accounts are still held to the rule.</summary>
    [Fact]
    public async Task A_new_account_cannot_take_a_name_like_that()
    {
        var client = factory.CreateClient();

        var res = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            username = "someone@example.com",
            email = "someone@example.com",
            password = Password,
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
