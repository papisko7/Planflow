using MediatR;
using PlanFlow.Application.Availability.Common;

namespace PlanFlow.Application.Availability.Queries.GetMemberCalendar;

/// <summary>One teammate's calendar as the caller may see it: their sharing level decides how much of the personal part is visible.</summary>
public record GetMemberCalendarQuery(Guid TeamId, Guid MemberUserId, Guid RequestingUserId, DateTime From, int Days) : IRequest<MemberCalendarDto>;
