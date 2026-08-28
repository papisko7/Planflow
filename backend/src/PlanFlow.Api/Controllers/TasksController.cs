using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlanFlow.Api.Common;
using PlanFlow.Api.Contracts;
using PlanFlow.Application.Tasks.Commands.CreateTask;
using PlanFlow.Application.Tasks.Commands.DeleteTask;
using PlanFlow.Application.Tasks.Commands.UpdateTask;
using PlanFlow.Application.Tasks.Queries.GetTaskDetail;
using PlanFlow.Application.Tasks.Queries.GetTasksByTeam;
using PlanFlow.Domain.Authorization;

namespace PlanFlow.Api.Controllers;

[ApiController]
[Authorize]
public class TasksController : ControllerBase
{
    private readonly ISender _sender;

    public TasksController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet("api/teams/{teamId:guid}/tasks")]
    [Authorize(Policy = nameof(Permission.ViewTasks))]
    public async Task<IActionResult> GetTeamTasks(Guid teamId, CancellationToken cancellationToken)
    {
        if (User.GetTeamId() != teamId)
        {
            return Forbid();
        }

        var tasks = await _sender.Send(new GetTasksByTeamQuery(teamId), cancellationToken);
        return Ok(tasks);
    }

    [HttpPost("api/teams/{teamId:guid}/tasks")]
    [Authorize(Policy = nameof(Permission.CreateTask))]
    public async Task<IActionResult> CreateTask(Guid teamId, CreateTaskRequest request, CancellationToken cancellationToken)
    {
        if (User.GetTeamId() != teamId)
        {
            return Forbid();
        }

        var task = await _sender.Send(
            new CreateTaskCommand(
                teamId,
                request.Title,
                request.Description,
                request.AssignedUserId,
                request.DeadlineUtc,
                request.ImpactScore,
                request.BlockedByTaskId,
                User.GetUserId()),
            cancellationToken);

        return CreatedAtAction(nameof(GetTask), new { taskId = task.Id }, task);
    }

    // Single-task routes below don't carry a {teamId} in the URL, so they can only check that the
    // caller holds the permission for SOME team (via the "team_role" claim) — not that it's for
    // THIS task's team. Closing that gap needs the multi-team JWT/team-switch redesign already
    // flagged as Phase 4.3 in PermissionAuthorizationHandler; team-scoped routes above already
    // enforce the stronger check cheaply because their teamId is known upfront.
    [HttpGet("api/tasks/{taskId:guid}")]
    [Authorize(Policy = nameof(Permission.ViewTasks))]
    public async Task<IActionResult> GetTask(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await _sender.Send(new GetTaskDetailQuery(taskId), cancellationToken);
        return Ok(task);
    }

    [HttpPut("api/tasks/{taskId:guid}")]
    [Authorize(Policy = nameof(Permission.EditAnyTask))]
    public async Task<IActionResult> UpdateTask(Guid taskId, UpdateTaskRequest request, CancellationToken cancellationToken)
    {
        var task = await _sender.Send(
            new UpdateTaskCommand(
                taskId,
                request.Title,
                request.Description,
                request.Status,
                request.AssignedUserId,
                request.DeadlineUtc,
                request.ImpactScore,
                request.BlockedByTaskId,
                request.ManualUrgencyOverride,
                request.ManualUrgencyOverrideExpiresAtUtc,
                User.GetUserId()),
            cancellationToken);

        return Ok(task);
    }

    [HttpDelete("api/tasks/{taskId:guid}")]
    [Authorize(Policy = nameof(Permission.DeleteTask))]
    public async Task<IActionResult> DeleteTask(Guid taskId, CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteTaskCommand(taskId), cancellationToken);
        return NoContent();
    }
}
