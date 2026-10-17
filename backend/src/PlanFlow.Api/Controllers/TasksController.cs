using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlanFlow.Api.Common;
using PlanFlow.Api.Contracts;
using PlanFlow.Application.Tasks.Commands.CreateTask;
using PlanFlow.Application.Tasks.Commands.DeleteTask;
using PlanFlow.Application.Tasks.Commands.UpdateTask;
using PlanFlow.Application.Tasks.Queries.GetTaskDetail;
using PlanFlow.Application.Tasks.Queries.GetTaskScoreHistory;
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

        // Only Owner/Admin hand work to other people; a Member's new task is always their own.
        var callerId = User.GetUserId();
        var canAssignOthers = User.HasPermission(Permission.EditAnyTask);
        if (!canAssignOthers && request.AssignedUserId is { } requestedAssignee && requestedAssignee != callerId)
        {
            return Forbid();
        }

        var task = await _sender.Send(
            new CreateTaskCommand(
                teamId,
                request.Title,
                request.Description,
                canAssignOthers ? request.AssignedUserId : callerId,
                request.DeadlineUtc,
                request.ImpactScore,
                request.BlockedByTaskId,
                User.GetUserId(),
                request.TaskType,
                request.StartUtc,
                request.EstimatedEffortHours,
                request.IsRecurring,
                request.RecurrenceIntervalHours,
                request.RecurrenceEndsAtUtc,
                request.UseAiEnhancement,
                request.EndUtc),
            cancellationToken);

        return CreatedAtAction(nameof(GetTask), new { taskId = task.Id }, task);
    }

    // Single-task routes below don't carry a {teamId} in the URL, so the [Authorize(Policy=...)]
    // attribute alone only proves the caller holds the permission for SOME team (via the
    // "team_role" claim) — not that it's for THIS task's team. We close that gap with an explicit
    // resource-ownership check against the task's real TeamId (Phase 4.3). A full fix would carry
    // {teamId} in the route like the team-scoped endpoints above; that needs the multi-team
    // JWT/team-switch redesign, out of scope here.
    [HttpGet("api/tasks/{taskId:guid}")]
    [Authorize(Policy = nameof(Permission.ViewTasks))]
    public async Task<IActionResult> GetTask(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await _sender.Send(new GetTaskDetailQuery(taskId), cancellationToken);

        if (User.GetTeamId() != task.Task.TeamId)
        {
            return Forbid();
        }

        return Ok(task);
    }

    // Full audit trail of the deterministic Urgency Score: one entry per computation, newest first.
    [HttpGet("api/tasks/{taskId:guid}/score-history")]
    [Authorize(Policy = nameof(Permission.ViewTasks))]
    public async Task<IActionResult> GetScoreHistory(Guid taskId, CancellationToken cancellationToken)
    {
        var existing = await _sender.Send(new GetTaskDetailQuery(taskId), cancellationToken);

        if (User.GetTeamId() != existing.Task.TeamId)
        {
            return Forbid();
        }

        var history = await _sender.Send(new GetTaskScoreHistoryQuery(taskId), cancellationToken);
        return Ok(history);
    }

    [HttpPut("api/tasks/{taskId:guid}")]
    [Authorize(Policy = nameof(Permission.EditOwnTask))]
    public async Task<IActionResult> UpdateTask(Guid taskId, UpdateTaskRequest request, CancellationToken cancellationToken)
    {
        var existing = await _sender.Send(new GetTaskDetailQuery(taskId), cancellationToken);

        if (User.GetTeamId() != existing.Task.TeamId)
        {
            return Forbid();
        }

        // A Member may edit a task that is already theirs (and not hand it to someone else), CLAIM an
        // unassigned one by assigning it to themselves, or ABANDON their own task by clearing the assignee
        // (it goes back to the unassigned pool). A task owned by somebody else is off limits:
        // only Owner/Admin can reassign it.
        if (!User.HasPermission(Permission.EditAnyTask))
        {
            var callerId = User.GetUserId();
            var isOwnTask = existing.Task.AssignedUserId == callerId && request.AssignedUserId == callerId;
            var isClaim = existing.Task.AssignedUserId is null && request.AssignedUserId == callerId;
            var isAbandon = existing.Task.AssignedUserId == callerId && request.AssignedUserId is null;
            if (!isOwnTask && !isClaim && !isAbandon)
            {
                return Forbid();
            }
        }

        // A client that predates the scheduling fields sends no TaskType: keep what is stored, otherwise an edit
        // of the title would silently turn an Event into a plain Task and wipe its effort and recurrence.
        var stored = existing.Task;
        var keepStored = request.TaskType is null;

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
                User.GetUserId(),
                keepStored ? stored.TaskType : request.TaskType!.Value,
                keepStored ? stored.StartUtc : request.StartUtc,
                keepStored ? stored.EstimatedEffortHours : request.EstimatedEffortHours,
                keepStored ? stored.IsRecurring : request.IsRecurring,
                keepStored ? stored.RecurrenceIntervalHours : request.RecurrenceIntervalHours,
                keepStored ? stored.RecurrenceEndsAtUtc : request.RecurrenceEndsAtUtc,
                keepStored ? stored.UseAiEnhancement : request.UseAiEnhancement,
                keepStored ? stored.EndUtc : request.EndUtc),
            cancellationToken);

        return Ok(task);
    }

    [HttpDelete("api/tasks/{taskId:guid}")]
    [Authorize(Policy = nameof(Permission.DeleteOwnTask))]
    public async Task<IActionResult> DeleteTask(Guid taskId, CancellationToken cancellationToken)
    {
        var existing = await _sender.Send(new GetTaskDetailQuery(taskId), cancellationToken);

        if (User.GetTeamId() != existing.Task.TeamId)
        {
            return Forbid();
        }

        // Owner/Admin delete any task; a Member only their own (assigned to them).
        if (!User.HasPermission(Permission.DeleteTask) && existing.Task.AssignedUserId != User.GetUserId())
        {
            return Forbid();
        }

        await _sender.Send(new DeleteTaskCommand(taskId, User.GetUserId()), cancellationToken);
        return NoContent();
    }
}
