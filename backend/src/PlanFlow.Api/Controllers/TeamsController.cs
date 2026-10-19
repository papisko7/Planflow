using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlanFlow.Api.Common;
using PlanFlow.Api.Contracts;
using PlanFlow.Application.Availability.Queries.GetMemberCalendar;
using PlanFlow.Application.Availability.Queries.GetTeamAvailability;
using PlanFlow.Application.Teams.Commands.AddTeamMember;
using PlanFlow.Application.Teams.Commands.ChangeMemberRole;
using PlanFlow.Application.Teams.Commands.UpdateTeamNotificationSettings;
using PlanFlow.Application.Teams.Commands.CreateTeam;
using PlanFlow.Application.Teams.Commands.DeleteTeam;
using PlanFlow.Application.Teams.Commands.RemoveTeamMember;
using PlanFlow.Application.Teams.Commands.UpdateTeam;
using PlanFlow.Application.Teams.Queries.GetTeamMembers;
using PlanFlow.Application.Teams.Queries.GetUserTeams;

namespace PlanFlow.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/teams")]
public class TeamsController : ControllerBase
{
    private readonly ISender _sender;

    public TeamsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost]
    public async Task<IActionResult> CreateTeam(CreateTeamRequest request, CancellationToken cancellationToken)
    {
        var team = await _sender.Send(new CreateTeamCommand(request.Name, request.Description, User.GetUserId()), cancellationToken);
        return CreatedAtAction(nameof(GetMyTeams), team);
    }

    [HttpGet]
    public async Task<IActionResult> GetMyTeams(CancellationToken cancellationToken)
    {
        var teams = await _sender.Send(new GetUserTeamsQuery(User.GetUserId()), cancellationToken);
        return Ok(teams);
    }

    // Like the member endpoints, the handler checks the caller's real membership row (Owner/Admin may edit).
    [HttpPut("{teamId:guid}")]
    public async Task<IActionResult> UpdateTeam(Guid teamId, UpdateTeamRequest request, CancellationToken cancellationToken)
    {
        var team = await _sender.Send(new UpdateTeamCommand(teamId, User.GetUserId(), request.Name, request.Description), cancellationToken);
        return Ok(team);
    }

    // Owner only; the handler enforces it.
    [HttpDelete("{teamId:guid}")]
    public async Task<IActionResult> DeleteTeam(Guid teamId, CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteTeamCommand(teamId, User.GetUserId()), cancellationToken);
        return NoContent();
    }

    // Membership (not a JWT claim) is verified inside the handler, for the same reason as AddMember.
    [HttpGet("{teamId:guid}/members")]
    public async Task<IActionResult> GetMembers(Guid teamId, CancellationToken cancellationToken)
    {
        var members = await _sender.Send(new GetTeamMembersQuery(teamId, User.GetUserId()), cancellationToken);
        return Ok(members);
    }

    // Who is free, busy or away, and how loaded they are. The handler checks the caller's membership; what a member shares of
    // their personal calendar is decided on the server by their own sharing level.
    [HttpGet("{teamId:guid}/availability")]
    public async Task<IActionResult> GetAvailability(
        Guid teamId, [FromQuery] DateTime? from, [FromQuery] int days = 7, CancellationToken cancellationToken = default)
    {
        var availability = await _sender.Send(
            new GetTeamAvailabilityQuery(teamId, User.GetUserId(), from ?? DateTime.UtcNow.Date, days), cancellationToken);
        return Ok(availability);
    }

    [HttpGet("{teamId:guid}/members/{userId:guid}/calendar")]
    public async Task<IActionResult> GetMemberCalendar(
        Guid teamId, Guid userId, [FromQuery] DateTime? from, [FromQuery] int days = 7, CancellationToken cancellationToken = default)
    {
        var calendar = await _sender.Send(
            new GetMemberCalendarQuery(teamId, userId, User.GetUserId(), from ?? DateTime.UtcNow.Date, days), cancellationToken);
        return Ok(calendar);
    }

    // No policy attribute here on purpose: AddTeamMemberCommandHandler already looks up the
    // requester's REAL membership row for THIS team and checks ManageMembers there (see its own
    // XML doc) — more correct than a JWT "team_role" claim that may have been issued for a
    // different team than the one in this route.
    [HttpPost("{teamId:guid}/members")]
    public async Task<IActionResult> AddMember(Guid teamId, AddTeamMemberRequest request, CancellationToken cancellationToken)
    {
        var member = await _sender.Send(
            new AddTeamMemberCommand(teamId, User.GetUserId(), request.Email, request.Role),
            cancellationToken);

        return Ok(member);
    }

    // Handler enforces ManageMembers plus the owner-only rules for Admin (same reasoning as AddMember).
    [HttpPut("{teamId:guid}/members/{userId:guid}/role")]
    public async Task<IActionResult> ChangeMemberRole(Guid teamId, Guid userId, ChangeMemberRoleRequest request, CancellationToken cancellationToken)
    {
        var member = await _sender.Send(new ChangeMemberRoleCommand(teamId, User.GetUserId(), userId, request.Role), cancellationToken);
        return Ok(member);
    }

    // The caller's own per-team e-mail mutes (any member may set them for themselves).
    [HttpPut("{teamId:guid}/notification-settings")]
    public async Task<IActionResult> UpdateNotificationSettings(Guid teamId, TeamNotificationSettingsRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new UpdateTeamNotificationSettingsCommand(teamId, User.GetUserId(), request.MuteNotifications, request.MuteChat), cancellationToken);
        return NoContent();
    }

    // Same reasoning as AddMember: the handler checks the caller's real membership for this team.
    // Also serves as "leave team" when userId is the caller's own id.
    [HttpDelete("{teamId:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid teamId, Guid userId, CancellationToken cancellationToken)
    {
        await _sender.Send(new RemoveTeamMemberCommand(teamId, User.GetUserId(), userId), cancellationToken);
        return NoContent();
    }
}
