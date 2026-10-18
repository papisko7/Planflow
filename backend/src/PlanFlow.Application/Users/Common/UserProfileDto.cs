namespace PlanFlow.Application.Users.Common;

public record UserProfileDto(Guid Id, string Email, string DisplayName, bool EmailVerified, string? TimeZoneId = null);
