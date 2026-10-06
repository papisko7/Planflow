namespace PlanFlow.Api.Contracts;

public record CreateTeamRequest(string Name, string? Description);

public record UpdateTeamRequest(string Name, string? Description);

public record AddTeamMemberRequest(string Email, Domain.Enums.TeamRole Role);

public record ChangeMemberRoleRequest(Domain.Enums.TeamRole Role);

public record TeamNotificationSettingsRequest(bool MuteNotifications, bool MuteChat);
