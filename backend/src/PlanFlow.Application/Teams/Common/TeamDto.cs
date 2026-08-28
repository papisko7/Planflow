using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Teams.Common;

public record TeamDto(Guid Id, string Name, string? Description, int MemberCount)
{
    public static TeamDto FromEntity(Team team) => new(
        team.Id, team.Name, team.Description, team.Members.Count);
}

public record TeamMemberDto(Guid Id, Guid UserId, string UserDisplayName, Domain.Enums.TeamRole Role, DateTime JoinedAtUtc)
{
    public static TeamMemberDto FromEntity(TeamMember member) => new(
        member.Id, member.UserId, member.User.DisplayName, member.Role, member.JoinedAtUtc);
}
