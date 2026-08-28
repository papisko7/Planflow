namespace PlanFlow.Api.Contracts;

public record CreateTeamRequest(string Name, string? Description);

public record AddTeamMemberRequest(Guid NewMemberUserId, Domain.Enums.TeamRole Role);
