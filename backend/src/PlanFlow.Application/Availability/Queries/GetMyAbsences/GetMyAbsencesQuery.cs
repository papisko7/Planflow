using MediatR;

namespace PlanFlow.Application.Availability.Queries.GetMyAbsences;

/// <summary>One stretch of the caller's own out-of-office time, as real UTC instants (an all-day absence is already read in their time zone).</summary>
public record MyAbsenceDto(Guid ItemId, DateTime StartUtc, DateTime EndUtc);

/// <summary>
/// The caller's own Out of office events from now on, in any of their teams and in their imported Google calendar. Nobody else can ask for
/// them: they are what the app uses to warn "this deadline falls while you are away", so the sharing level does not apply to one's own data.
/// </summary>
public record GetMyAbsencesQuery(Guid UserId) : IRequest<IReadOnlyList<MyAbsenceDto>>;
