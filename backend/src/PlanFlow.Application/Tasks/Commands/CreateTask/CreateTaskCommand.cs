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
    int ImpactScore,
    Guid? BlockedByTaskId,
    Guid? CreatedByUserId) : IRequest<TaskDto>;
