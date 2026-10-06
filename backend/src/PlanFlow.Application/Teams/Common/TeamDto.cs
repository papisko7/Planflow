using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Teams.Common;

/// <summary><paramref name="MuteNotifications"/>/<paramref name="MuteChat"/> are the CALLER's per-team e-mail mutes (not a property of the team itself).</summary>
public record TeamDto(
    Guid Id, string Name, string? Description, int MemberCount, bool IsPersonal = false,
    bool MuteNotifications = false, bool MuteChat = false)
{
    public static TeamDto FromEntity(Team team) => new(
        team.Id, team.Name, team.Description, team.Members.Count, team.IsPersonal);
}

public record TeamMemberDto(Guid Id, Guid UserId, string UserDisplayName, Domain.Enums.TeamRole Role, DateTime JoinedAtUtc)
{
    public static TeamMemberDto FromEntity(TeamMember member) => new(
        member.Id, member.UserId, member.User.DisplayName, member.Role, member.JoinedAtUtc);
}
