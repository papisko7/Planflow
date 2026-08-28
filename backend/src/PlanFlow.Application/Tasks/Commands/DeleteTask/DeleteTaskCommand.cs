using MediatR;

namespace PlanFlow.Application.Tasks.Commands.DeleteTask;

public record DeleteTaskCommand(Guid TaskId) : IRequest;
