using MediatR;
using PlanFlow.Application.Tasks.Common;

namespace PlanFlow.Application.Tasks.Commands.CreateTask;

/// <summary>Creates a new task on a team and computes its initial Urgency Score.</summary>
public record CreateTaskCommand(
    Guid TeamId,
    string Title,
    string? Description,
    Guid? AssignedUserId,
    DateTime? DeadlineUtc,
    int? ImpactScore,
    Guid? BlockedByTaskId,
    Guid? CreatedByUserId,
    Domain.Enums.TaskType TaskType = Domain.Enums.TaskType.Task,
    DateTime? StartUtc = null,
    double? EstimatedEffortHours = null,
    bool IsRecurring = false,
    int? RecurrenceIntervalHours = null,
    DateTime? RecurrenceEndsAtUtc = null,
    bool UseAiEnhancement = false,
    DateTime? EndUtc = null) : IRequest<TaskDto>, ITaskScheduleFields;
