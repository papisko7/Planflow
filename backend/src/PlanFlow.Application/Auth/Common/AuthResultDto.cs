namespace PlanFlow.Application.Auth.Common;

public record AuthResultDto(
    Guid UserId,
    string Email,
    string DisplayName,
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken);
