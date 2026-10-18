using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Interfaces;

namespace PlanFlow.Application.ItemLists;

/// <summary>The team's own lists, by name.</summary>
public record GetItemListsQuery(Guid TeamId) : IRequest<IReadOnlyList<ItemListDto>>;

public class GetItemListsQueryHandler : IRequestHandler<GetItemListsQuery, IReadOnlyList<ItemListDto>>
{
    private readonly IApplicationDbContext _context;

    public GetItemListsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ItemListDto>> Handle(GetItemListsQuery request, CancellationToken cancellationToken)
    {
        var lists = await _context.ItemLists.AsNoTracking()
            .Where(l => l.TeamId == request.TeamId)
            .OrderBy(l => l.Name)
            .ToListAsync(cancellationToken);
        return lists.Select(ItemListDto.FromEntity).ToList();
    }
}
