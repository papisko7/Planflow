using FluentValidation;
using PlanFlow.Application.Availability.Common;

namespace PlanFlow.Application.Availability.Queries.GetTeamAvailability;

public class GetTeamAvailabilityQueryValidator : AbstractValidator<GetTeamAvailabilityQuery>
{
    public GetTeamAvailabilityQueryValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();
        RuleFor(x => x.RequestingUserId).NotEmpty();
        RuleFor(x => x.Days).InclusiveBetween(1, MemberCalendars.MaxDays);
    }
}
