namespace Linkbelli.Api.Endpoints;

public record RegisterRequest(string Username, string Email, string Password);

/// <summary>Login accepts either a username or an email in the single Login field.</summary>
public record LoginRequest(string Login, string Password);

public record RefreshRequest(string RefreshToken);
