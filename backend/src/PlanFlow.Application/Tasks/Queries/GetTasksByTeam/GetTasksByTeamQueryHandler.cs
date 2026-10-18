using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Caching;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Tasks.Common;

namespace PlanFlow.Application.Tasks.Queries.GetTasksByTeam;

public class GetTasksByTeamQueryHandler : IRequestHandler<GetTasksByTeamQuery, IReadOnlyList<TaskDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;

    public GetTasksByTeamQueryHandler(IApplicationDbContext context, ICacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<IReadOnlyList<TaskDto>> Handle(GetTasksByTeamQuery request, CancellationToken cancellationToken)
    {
        var cacheKey = CacheKeys.TeamTasks(request.TeamId);
        var cached = await _cache.GetAsync<List<TaskDto>>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        // Materialize entities first, then map to TaskDto in memory: EF Core cannot translate
        // a call to a factory method (TaskDto.FromEntity) into SQL inside a Select projection.
        // The latest ReasonText comes from the same query (indexed on item + time), so the list can show "why".
        var rows = await _context.CalendarItems
            .AsNoTracking()
            .Where(t => t.TeamId == request.TeamId)
            .OrderByDescending(t => t.CurrentUrgencyScore)
            .Select(t => new
            {
                Item = t,
                Reason = t.UrgencyScoreLogs.OrderByDescending(l => l.CreatedAtUtc).Select(l => l.ReasonText).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        // An informational item (Out of office, ...) has no score and so no "why"; an old log from before it was one must not show.
        var result = rows
            .Select(r => TaskDto.FromEntity(r.Item) with { ReasonText = CalendarItemUrgencyScoreFactory.IsRanked(r.Item) ? r.Reason : null })
            .ToList();
        await _cache.SetAsync(cacheKey, result, CacheKeys.TeamTasksTtl, cancellationToken);
        return result;
    }
}
