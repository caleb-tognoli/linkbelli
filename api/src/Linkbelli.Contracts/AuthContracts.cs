namespace Linkbelli.Contracts;

public record RegisterRequest(string Username, string Email, string Password);

/// <summary>Login accepts either a username or an email in the single Login field.</summary>
public record LoginRequest(string Login, string Password);

public record RefreshRequest(string RefreshToken);

/// <summary>
/// Starts a password reset. <c>Login</c> takes a username or an email, because somebody who has
/// forgotten their password may not remember which they signed up with.
/// </summary>
public record ForgotPasswordRequest(string Login);

/// <summary>Finishes one. The token is whatever came in the link, unchanged.</summary>
public record ResetPasswordRequest(string Email, string Token, string NewPassword);
