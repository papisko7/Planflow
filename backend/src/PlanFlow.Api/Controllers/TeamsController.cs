using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlanFlow.Api.Common;
using PlanFlow.Api.Contracts;
using PlanFlow.Application.Teams.Commands.AddTeamMember;
using PlanFlow.Application.Teams.Commands.CreateTeam;
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

    // No policy attribute here on purpose: AddTeamMemberCommandHandler already looks up the
    // requester's REAL membership row for THIS team and checks ManageMembers there (see its own
    // XML doc) — more correct than a JWT "team_role" claim that may have been issued for a
    // different team than the one in this route.
    [HttpPost("{teamId:guid}/members")]
    public async Task<IActionResult> AddMember(Guid teamId, AddTeamMemberRequest request, CancellationToken cancellationToken)
    {
        var member = await _sender.Send(
            new AddTeamMemberCommand(teamId, User.GetUserId(), request.NewMemberUserId, request.Role),
            cancellationToken);

        return Ok(member);
    }
}
