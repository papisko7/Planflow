using MediatR;
using PlanFlow.Application.Availability.Common;
using PlanFlow.Application.Common.Interfaces;

namespace PlanFlow.Application.Availability.Queries.GetTeamAvailability;

public class GetTeamAvailabilityQueryHandler : IRequestHandler<GetTeamAvailabilityQuery, TeamAvailabilityDto>
{
    private readonly IApplicationDbContext _context;

    public GetTeamAvailabilityQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<TeamAvailabilityDto> Handle(GetTeamAvailabilityQuery request, CancellationToken cancellationToken)
    {
        var from = request.From.Date;
        var members = await MemberCalendars.LoadAsync(
            _context, request.TeamId, request.RequestingUserId, null, from, request.Days, cancellationToken);

        var now = DateTime.UtcNow;
        var rows = members.Select(m => MemberCalendars.Summarize(m, from, request.Days, now)).ToList();
        return new TeamAvailabilityDto(request.TeamId, from, request.Days, now, rows);
    }
}
