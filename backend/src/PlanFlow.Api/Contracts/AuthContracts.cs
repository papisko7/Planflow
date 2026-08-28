namespace PlanFlow.Api.Contracts;

public record RegisterRequest(string Email, string Password, string DisplayName);

/// <summary><paramref name="TeamId"/> is optional — omit it to log in without a team context (see <see cref="PlanFlow.Application.Auth.Commands.Login.LoginCommand"/>).</summary>
public record LoginRequest(string Email, string Password, Guid? TeamId);

public record RefreshRequest(Guid UserId, string RefreshToken);
