namespace PlanFlow.Api.Contracts;

public record RegisterRequest(string Email, string Password, string DisplayName);

/// <summary><paramref name="TeamId"/> is optional — omit it to log in without a team context (see <see cref="PlanFlow.Application.Auth.Commands.Login.LoginCommand"/>).</summary>
public record LoginRequest(string Email, string Password, Guid? TeamId);

/// <summary><paramref name="TeamId"/> is optional — pass it to re-scope the new access token to a team without re-collecting the password.</summary>
public record RefreshRequest(Guid UserId, string RefreshToken, Guid? TeamId = null);
