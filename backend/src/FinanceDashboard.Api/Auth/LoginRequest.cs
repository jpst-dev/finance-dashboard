namespace FinanceDashboard.Api.Auth;

/// <summary>Login payload. Never log any of its fields.</summary>
public sealed record LoginRequest(string Email, string Password)
{
    // Keep the password out of ToString() in case the record ever reaches a log or exception message.
    public override string ToString() => nameof(LoginRequest);
}
