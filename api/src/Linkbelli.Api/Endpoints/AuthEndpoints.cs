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

            var user = new ApplicationUser { UserName = req.Username.Trim(), Email = req.Email.Trim() };
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
    }
}
