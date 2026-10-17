namespace PlanFlow.Api.Contracts;

public record RegisterRequest(string Email, string Password, string DisplayName, string? DeviceId = null, string? DeviceName = null);

/// <summary><paramref name="TeamId"/> is optional — omit it to log in without a team context (see <see cref="PlanFlow.Application.Auth.Commands.Login.LoginCommand"/>).</summary>
public record LoginRequest(string Email, string Password, Guid? TeamId, string? DeviceId = null, string? DeviceName = null);

/// <summary><paramref name="TeamId"/> is optional — pass it to re-scope the new access token to a team without re-collecting the password.</summary>
public record RefreshRequest(Guid UserId, string RefreshToken, Guid? TeamId = null);

public record IdentifyRequest(string Email);

public record IdentifyResponse(Guid UserId);

public record ForgotPasswordRequest(string Email);

/// <summary><paramref name="Code"/> is the 6-digit code from the reset e-mail.</summary>
public record ResetPasswordRequest(string Email, string Code, string NewPassword);
