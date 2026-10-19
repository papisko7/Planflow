using FluentValidation;
using PlanFlow.Application.Availability.Common;

namespace PlanFlow.Application.Availability.Queries.GetMemberCalendar;

public class GetMemberCalendarQueryValidator : AbstractValidator<GetMemberCalendarQuery>
{
    public GetMemberCalendarQueryValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();
        RuleFor(x => x.MemberUserId).NotEmpty();
        RuleFor(x => x.RequestingUserId).NotEmpty();
        RuleFor(x => x.Days).InclusiveBetween(1, MemberCalendars.MaxDays);
    }
}
