using Linkbelli.Application.Email;
using Linkbelli.Application.Identity;
using Linkbelli.Contracts;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Linkbelli.Api.Endpoints;

/// <summary>
/// Custom auth endpoints (replacing MapIdentityApi) so users can register with a
/// username and log in with either their username or email. Bearer access/refresh
/// tokens are still issued by Identity's bearer-token handler via Results.SignIn.
/// </summary>
public static class AuthEndpoints
{
    // A precomputed hash of a random password, used to spend hashing time on the
    // user-not-found login path so timing doesn't reveal whether a login exists.
    private static readonly string DummyPasswordHash =
        new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), Guid.NewGuid().ToString());

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/register", async (RegisterRequest req, UserManager<ApplicationUser> users) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(req.Username)) errors["username"] = ["Username is required."];
            if (string.IsNullOrWhiteSpace(req.Email)) errors["email"] = ["Email is required."];
            if (string.IsNullOrWhiteSpace(req.Password)) errors["password"] = ["Password is required."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var user = new ApplicationUser
            {
                UserName = req.Username.Trim(),
                Email = req.Email.Trim(),
                CreatedAt = DateTimeOffset.UtcNow,
            };
            var result = await users.CreateAsync(user, req.Password);
            if (!result.Succeeded)
            {
                return Results.ValidationProblem(result.Errors
                    .GroupBy(e => e.Code)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
            }

            return Results.Ok();
        }).AllowAnonymous().WithName("Register");

        group.MapPost("/login", async (
            LoginRequest req,
            UserManager<ApplicationUser> users,
            SignInManager<ApplicationUser> signIn) =>
        {
            if (string.IsNullOrWhiteSpace(req.Login) || string.IsNullOrWhiteSpace(req.Password))
            {
                return Results.Problem("Login and password are required.", statusCode: StatusCodes.Status401Unauthorized);
            }

            var user = await users.FindByNameAsync(req.Login) ?? await users.FindByEmailAsync(req.Login);
            if (user is null)
            {
                // Run a hash verification against a throwaway hash so a non-existent login takes
                // roughly the same time as a real one — closes the username-enumeration timing gap.
                users.PasswordHasher.VerifyHashedPassword(new ApplicationUser(), DummyPasswordHash, req.Password);
                return Results.Problem("Invalid credentials.", statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await signIn.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
            if (!result.Succeeded)
            {
                var reason = result.IsLockedOut ? "Account is locked out." : "Invalid credentials.";
                return Results.Problem(reason, statusCode: StatusCodes.Status401Unauthorized);
            }

            var principal = await signIn.CreateUserPrincipalAsync(user);
            return Results.SignIn(principal, authenticationScheme: IdentityConstants.BearerScheme);
        }).AllowAnonymous().WithName("Login");

        group.MapPost("/refresh", async (
            RefreshRequest req,
            SignInManager<ApplicationUser> signIn,
            IOptionsMonitor<BearerTokenOptions> bearerOptions,
            TimeProvider clock) =>
        {
            var protector = bearerOptions.Get(IdentityConstants.BearerScheme).RefreshTokenProtector;
            var ticket = protector.Unprotect(req.RefreshToken);

            if (ticket?.Properties?.ExpiresUtc is not { } expiresUtc
                || clock.GetUtcNow() >= expiresUtc
                || await signIn.ValidateSecurityStampAsync(ticket.Principal) is not { } user)
            {
                return Results.Challenge(authenticationSchemes: [IdentityConstants.BearerScheme]);
            }

            var principal = await signIn.CreateUserPrincipalAsync(user);
            return Results.SignIn(principal, authenticationScheme: IdentityConstants.BearerScheme);
        }).AllowAnonymous().WithName("Refresh");

        // A forgotten password used to mean the account was gone; there was no way back at all.
        group.MapPost("/forgot-password", async (
            ForgotPasswordRequest req, IPasswordResetService resets, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Login))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["login"] = ["A username or email is required."],
                });
            }

            if (!resets.CanSend)
            {
                // Said out loud rather than silently accepted: an instance with no mail
                // configured cannot do this, and pretending otherwise leaves somebody waiting
                // forever for a message nobody ever tried to send. Reveals nothing about any
                // account — only that this deployment has no mail set up.
                return Results.Problem(
                    "This Linkbelli has no mail configured, so it cannot send a reset link.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            await resets.RequestAsync(req.Login, ct);

            // The same answer whether or not that account exists. Anything else turns this
            // endpoint into a way to find out who has one.
            return Results.Accepted();
        })
            .AllowAnonymous()
            .RequireRateLimiting("sensitive")
            .WithName("ForgotPassword");

        group.MapPost("/reset-password", async (
            ResetPasswordRequest req, IPasswordResetService resets, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(req.Email)) errors["email"] = ["Email is required."];
            if (string.IsNullOrWhiteSpace(req.Token)) errors["token"] = ["Token is required."];
            if (string.IsNullOrWhiteSpace(req.NewPassword)) errors["newPassword"] = ["A new password is required."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var (ok, failures) = await resets.ResetAsync(req.Email, req.Token, req.NewPassword, ct);

            return ok
                ? Results.NoContent()
                : Results.ValidationProblem(new Dictionary<string, string[]> { ["password"] = failures });
        })
            .AllowAnonymous()
            .RequireRateLimiting("sensitive")
            .WithName("ResetPassword");
    }
}
