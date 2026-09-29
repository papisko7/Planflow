using MediatR;
using PlanFlow.Application.Tasks.Common;

namespace PlanFlow.Application.Tasks.Queries.GetTaskScoreHistory;

/// <summary>Returns every Urgency Score computation logged for a task, newest first.</summary>
public record GetTaskScoreHistoryQuery(Guid TaskId) : IRequest<IReadOnlyList<UrgencyScoreBreakdownDto>>;
