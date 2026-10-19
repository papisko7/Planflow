using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Availability.Common;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Enums;
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

        var finished = await LoadFinishedTasksAsync(request, from, cancellationToken);

        return new MemberCalendarDto(request.TeamId, summary, member.TimeZoneId, entries, tasks, finished);
    }

    // Same one-day padding as the open tasks and events: the real moment of a deadline depends on its owner's time zone.
    private async Task<List<MemberTaskDto>> LoadFinishedTasksAsync(GetMemberCalendarQuery request, DateTime from, CancellationToken cancellationToken)
    {
        var windowStart = DateTime.SpecifyKind(from.AddDays(-1), DateTimeKind.Utc);
        var windowEnd = DateTime.SpecifyKind(from.AddDays(request.Days + 1), DateTimeKind.Utc);

        var done = await _context.CalendarItems
            .AsNoTracking()
            .Where(i => i.TeamId == request.TeamId
                && i.TaskType == TaskType.Task
                && i.Status == Domain.Enums.TaskStatus.Completed
                && i.DeadlineUtc != null && i.DeadlineUtc >= windowStart && i.DeadlineUtc < windowEnd
                && (i.AssignedUserId == request.MemberUserId || i.Participants.Any(p => p.UserId == request.MemberUserId)))
            .OrderBy(i => i.DeadlineUtc)
            .ToListAsync(cancellationToken);

        return done
            .Select(t => new MemberTaskDto(
                t.Id, t.Title, t.DeadlineUtc!.Value, t.IsAllDay, t.EstimatedEffortHours is > 0 ? t.EstimatedEffortHours : null,
                t.Status, t.CurrentUrgencyScore))
            .ToList();
    }

    private static MemberCalendarEntryDto ToEntry(TimeBlock b, string source) =>
        new(b.ItemId, source, b.Kind, b.StartUtc, b.EndUtc, b.IsAllDay, b.Title, b.Location);
}
