using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Availability.Common;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;

namespace PlanFlow.Application.Availability.Queries.GetMyAbsences;

public class GetMyAbsencesQueryHandler : IRequestHandler<GetMyAbsencesQuery, IReadOnlyList<MyAbsenceDto>>
{
    /// <summary>How far ahead absences are listed: a deadline further away than this is not warned about.</summary>
    public const int HorizonDays = 180;

    private readonly IApplicationDbContext _context;

    public GetMyAbsencesQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<MyAbsenceDto>> Handle(GetMyAbsencesQuery request, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var since = nowUtc.AddDays(-1); // an absence that began earlier and still runs
        var until = nowUtc.AddDays(HorizonDays);

        var zoneId = await _context.Users.AsNoTracking()
            .Where(u => u.Id == request.UserId)
            .Select(u => u.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);
        var zone = AllDayTime.ZoneOrUtc(zoneId);

        var events = await _context.CalendarItems.AsNoTracking()
            .Where(i => i.AssignedUserId == request.UserId
                && i.TaskType == TaskType.Event
                && i.EventTemplate == EventTemplate.OutOfOffice
                && i.Status != Domain.Enums.TaskStatus.Cancelled
                && i.StartUtc != null && i.StartUtc < until && (i.EndUtc ?? i.StartUtc) >= since)
            .ToListAsync(cancellationToken);

        return events
            .Select(MemberCalendars.TeamBlockOf)
            .OfType<TimeBlock>()
            .Select(b =>
            {
                var (start, end) = b.Interval(zone);
                return new MyAbsenceDto(b.ItemId, start, end);
            })
            .Where(a => a.EndUtc > nowUtc)
            .OrderBy(a => a.StartUtc)
            .ToList();
    }
}
