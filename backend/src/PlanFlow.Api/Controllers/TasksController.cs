using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlanFlow.Api.Common;
using PlanFlow.Api.Contracts;
using PlanFlow.Application.Tasks.Commands.BulkTasks;
using PlanFlow.Application.Tasks.Commands.CreateTask;
using PlanFlow.Application.Tasks.Commands.DeleteTask;
using PlanFlow.Application.Tasks.Commands.LeaveTask;
using PlanFlow.Application.Tasks.Commands.UpdateTask;
using PlanFlow.Application.Tasks.Queries.GetTaskDetail;
using PlanFlow.Application.Tasks.Queries.GetTaskScoreHistory;
using PlanFlow.Application.Tasks.Queries.GetTasksByTeam;
using PlanFlow.Domain.Authorization;
using PlanFlow.Domain.Enums;

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
                request.RecurrenceFrequency,
                request.RecurrenceInterval,
                request.RecurrenceWeekdays,
                request.RecurrenceEndsAtUtc,
                request.EndUtc,
                request.EventTemplate,
                request.IsAllDay,
                request.Location,
                request.ColorKey,
                request.IsBusy,
                request.Reminders,
                request.ParticipantUserIds,
                request.ItemListId,
                canAssignOthers),
            cancellationToken);

        return CreatedAtAction(nameof(GetTask), new { taskId = task.Id }, task);
    }

    [HttpPost("api/teams/{teamId:guid}/tasks/bulk-delete")]
    [Authorize(Policy = nameof(Permission.DeleteOwnTask))]
    public async Task<IActionResult> BulkDelete(Guid teamId, BulkTasksRequest request, CancellationToken cancellationToken)
    {
        if (User.GetTeamId() != teamId)
        {
            return Forbid();
        }

        var result = await _sender.Send(
            new BulkDeleteTasksCommand(teamId, request.TaskIds, User.GetUserId(), User.HasPermission(Permission.DeleteTask)), cancellationToken);
        return Ok(result);
    }

    [HttpPost("api/teams/{teamId:guid}/tasks/bulk-status")]
    [Authorize(Policy = nameof(Permission.EditOwnTask))]
    public async Task<IActionResult> BulkSetStatus(Guid teamId, BulkStatusRequest request, CancellationToken cancellationToken)
    {
        if (User.GetTeamId() != teamId)
        {
            return Forbid();
        }

        var result = await _sender.Send(
            new BulkSetStatusCommand(teamId, request.TaskIds, request.Status, User.GetUserId(), User.HasPermission(Permission.EditAnyTask)), cancellationToken);
        return Ok(result);
    }

    [HttpPost("api/teams/{teamId:guid}/tasks/bulk-move")]
    [Authorize(Policy = nameof(Permission.EditOwnTask))]
    public async Task<IActionResult> BulkMoveToList(Guid teamId, BulkMoveRequest request, CancellationToken cancellationToken)
    {
        if (User.GetTeamId() != teamId)
        {
            return Forbid();
        }

        var result = await _sender.Send(
            new BulkMoveToListCommand(teamId, request.TaskIds, request.ItemListId, User.GetUserId(), User.HasPermission(Permission.EditAnyTask)), cancellationToken);
        return Ok(result);
    }

    // Single-task routes carry no {teamId}, so [Authorize(Policy=...)] only proves the caller holds the permission for SOME team
    // (the "team_role" claim), not for THIS task's team. The handler closes the gap by checking the task's real TeamId.
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

        // Who may change the assignee (a Member: keep their own task, claim an unassigned one, give back one they took themselves;
        // never one an Owner/Admin assigned) is decided by TaskAssignmentPolicy inside the command, which gets the caller's role.

        var stored = existing.Task;
        var keepStored = request.TaskType is null;
        var keepTemplate = request.EventTemplate is null; // same for a client that predates the template fields

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
                keepStored ? stored.RecurrenceFrequency : request.RecurrenceFrequency,
                keepStored ? stored.RecurrenceInterval : request.RecurrenceInterval,
                keepStored ? stored.RecurrenceWeekdays : request.RecurrenceWeekdays,
                keepStored ? stored.RecurrenceEndsAtUtc : request.RecurrenceEndsAtUtc,
                keepStored ? stored.EndUtc : request.EndUtc,
                keepTemplate ? stored.EventTemplate : request.EventTemplate!.Value,
                keepTemplate ? stored.IsAllDay : request.IsAllDay,
                keepTemplate ? stored.Location : request.Location,
                keepTemplate ? stored.ColorKey : request.ColorKey,
                keepTemplate ? stored.IsBusy : request.IsBusy,
                request.Reminders,
                request.ParticipantUserIds,
                keepStored ? stored.ItemListId : request.ItemListId,
                User.HasPermission(Permission.EditAnyTask),
                request.Scope),
            cancellationToken);

        return Ok(task);
    }

    // A guest of an event / co-contractor of a task steps out of it. Any member of the team can ask: the command only ever ends the
    // caller's own participation (404 when they are not one), so nobody can remove somebody else this way.
    [HttpDelete("api/tasks/{taskId:guid}/participants/me")]
    [Authorize(Policy = nameof(Permission.ViewTasks))]
    public async Task<IActionResult> LeaveTask(Guid taskId, CancellationToken cancellationToken)
    {
        var existing = await _sender.Send(new GetTaskDetailQuery(taskId), cancellationToken);

        if (User.GetTeamId() != existing.Task.TeamId)
        {
            return Forbid();
        }

        await _sender.Send(new LeaveTaskCommand(taskId, User.GetUserId()), cancellationToken);
        return NoContent();
    }

    [HttpDelete("api/tasks/{taskId:guid}")]
    [Authorize(Policy = nameof(Permission.DeleteOwnTask))]
    public async Task<IActionResult> DeleteTask(Guid taskId, [FromQuery] SeriesScope scope, CancellationToken cancellationToken)
    {
        var existing = await _sender.Send(new GetTaskDetailQuery(taskId), cancellationToken);

        if (User.GetTeamId() != existing.Task.TeamId)
        {
            return Forbid();
        }

        if (!User.HasPermission(Permission.DeleteTask) && existing.Task.AssignedUserId != User.GetUserId())
        {
            return Forbid();
        }

        await _sender.Send(new DeleteTaskCommand(taskId, User.GetUserId(), scope, User.HasPermission(Permission.DeleteTask)), cancellationToken);
        return NoContent();
    }
}
