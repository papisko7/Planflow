using MediatR;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Tasks.Commands.DeleteTask;

/// <summary>
/// <paramref name="DeletedByUserId"/> is who deleted it, so the assignee can be told (and the deleter is not e-mailed about their own action).
/// For an item that repeats, <paramref name="Scope"/> says which occurrences go (only this one, this and the following ones, or all);
/// <paramref name="CanDeleteAny"/> is false for a Member, who may only delete the occurrences assigned to them.
/// </summary>
public record DeleteTaskCommand(
    Guid TaskId, Guid? DeletedByUserId = null, SeriesScope Scope = SeriesScope.This, bool CanDeleteAny = true) : IRequest;
