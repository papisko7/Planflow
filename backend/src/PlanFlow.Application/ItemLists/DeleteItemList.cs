using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Caching;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.ItemLists;

/// <summary>Deletes a list (same permission as renaming it). The items in it are kept and just lose the grouping.</summary>
public record DeleteItemListCommand(Guid TeamId, Guid ListId, Guid CallerUserId, bool CanManageAny) : IRequest;

public class DeleteItemListCommandHandler : IRequestHandler<DeleteItemListCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;

    public DeleteItemListCommandHandler(IApplicationDbContext context, ICacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task Handle(DeleteItemListCommand request, CancellationToken cancellationToken)
    {
        var list = await _context.ItemLists.FirstOrDefaultAsync(l => l.Id == request.ListId && l.TeamId == request.TeamId, cancellationToken)
            ?? throw new NotFoundException(nameof(ItemList), request.ListId);

        if (!request.CanManageAny && list.CreatedByUserId != request.CallerUserId)
        {
            throw new ForbiddenAccessException("Only the person who made the list, or a team owner or admin, can delete it.");
        }

        // The database would clear the link by itself (set null); doing it here keeps the tracked items right in the same save.
        var items = await _context.CalendarItems.Where(t => t.ItemListId == list.Id).ToListAsync(cancellationToken);
        foreach (var item in items)
        {
            item.ItemListId = null;
        }

        _context.ItemLists.Remove(list);
        await _context.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CacheKeys.TeamTasks(list.TeamId), cancellationToken);
    }
}
