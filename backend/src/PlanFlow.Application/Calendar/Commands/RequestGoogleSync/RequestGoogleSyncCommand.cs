using MediatR;

namespace PlanFlow.Application.Calendar.Commands.RequestGoogleSync;

/// <summary>"Sync now" pressed by <paramref name="UserId"/>. The answer is true when a sync may start, false when the last one was moments ago.</summary>
public record RequestGoogleSyncCommand(Guid UserId) : IRequest<bool>;
