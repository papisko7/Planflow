using MediatR;
using PlanFlow.Application.Tasks.Common;

namespace PlanFlow.Application.Tasks.Commands.UpdateTask;

/// <summary>Overwrites a task's mutable fields and recalculates its Urgency Score.</summary>
public record UpdateTaskCommand(
    Guid TaskId,
    string Title,
    string? Description,
    Domain.Enums.TaskStatus Status,
    Guid? AssignedUserId,
    DateTime? DeadlineUtc,
    int ImpactScore,
    Guid? BlockedByTaskId,
    double? ManualUrgencyOverride,
    DateTime? ManualUrgencyOverrideExpiresAtUtc,
    Guid? UpdatedByUserId) : IRequest<TaskDto>;
