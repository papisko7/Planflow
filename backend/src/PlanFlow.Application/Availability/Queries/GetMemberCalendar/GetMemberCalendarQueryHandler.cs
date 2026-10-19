using MediatR;
using PlanFlow.Application.Availability.Common;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Services;

namespace PlanFlow.Application.Availability.Queries.GetMemberCalendar;

public class GetMemberCalendarQueryHandler : IRequestHandler<GetMemberCalendarQuery, MemberCalendarDto>
{
    private readonly IApplicationDbContext _context;

    public GetMemberCalendarQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<MemberCalendarDto> Handle(GetMemberCalendarQuery request, CancellationToken cancellationToken)
    {
        var from = request.From.Date;
        var member = (await MemberCalendars.LoadAsync(
            _context, request.TeamId, request.RequestingUserId, request.MemberUserId, from, request.Days, cancellationToken))[0];

        var summary = MemberCalendars.Summarize(member, from, request.Days, DateTime.UtcNow);

        var entries = member.PersonalBlocks.Select(b => ToEntry(b, "Personal"))
            .Concat(member.TeamBlocks.Select(b => ToEntry(b, "Team")))
            .OrderBy(e => e.StartUtc)
            .ToList();

        var tasks = member.Tasks
            .Select(t => new MemberTaskDto(t.ItemId, t.Title, t.DeadlineStoredUtc, t.IsAllDay, t.EffortHours, t.Status, t.UrgencyScore))
            .ToList();

        return new MemberCalendarDto(request.TeamId, summary, member.TimeZoneId, entries, tasks);
    }

    private static MemberCalendarEntryDto ToEntry(TimeBlock b, string source) =>
        new(b.ItemId, source, b.Kind, b.StartUtc, b.EndUtc, b.IsAllDay, b.Title, b.Location);
}
