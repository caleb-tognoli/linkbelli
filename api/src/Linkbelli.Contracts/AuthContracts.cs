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

/// <summary>Finishes proving an address belongs to whoever signed up with it.</summary>
public record ConfirmEmailRequest(string Email, string Token);

/// <summary>Asks for another confirmation link.</summary>
public record ResendConfirmationRequest(string Email);

/// <summary>
/// Asks for the account to go. Password-confirmed: a session left open on a shared machine
/// should not be enough to end somebody's account.
/// </summary>
public record DeleteAccountRequest(string Password);

/// <summary>When the account will actually be removed, and what happens in between.</summary>
public record AccountDeletionScheduled(DateTimeOffset DeletesAt);
