using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Tasks.Commands.DeleteTask;

public class DeleteTaskCommandHandler : IRequestHandler<DeleteTaskCommand>
{
    private readonly IApplicationDbContext _context;

    public DeleteTaskCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(DeleteTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await _context.Tasks
            .FirstOrDefaultAsync(t => t.Id == request.TaskId, cancellationToken)
            ?? throw new NotFoundException(nameof(TaskItem), request.TaskId);

        // TaskHistory/UrgencyScoreLog rows cascade-delete with the task (see TaskHistoryConfiguration),
        // so no separate "Deleted" audit entry is written here — it would be removed in the same transaction.
        _context.Tasks.Remove(task);

        await _context.SaveChangesAsync(cancellationToken);
    }
}
