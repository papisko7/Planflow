using MediatR;

namespace PlanFlow.Application.Tasks.Commands.DeleteTask;

/// <summary><paramref name="DeletedByUserId"/> is who deleted it, so the assignee can be told (and the deleter is not e-mailed about their own action).</summary>
public record DeleteTaskCommand(Guid TaskId, Guid? DeletedByUserId = null) : IRequest;
