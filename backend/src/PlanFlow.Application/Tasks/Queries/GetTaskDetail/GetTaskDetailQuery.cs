using MediatR;
using PlanFlow.Application.Tasks.Common;

namespace PlanFlow.Application.Tasks.Queries.GetTaskDetail;

/// <summary>Fetches one task plus its latest Urgency Score breakdown, for the task detail screen.</summary>
public record GetTaskDetailQuery(Guid TaskId) : IRequest<TaskDetailDto>;
