using MediatR;

namespace PlanFlow.Application.Calendar.Queries.GetGoogleCalendarStatus;

public record GetGoogleCalendarStatusQuery(Guid UserId) : IRequest<GoogleCalendarStatusDto>;

/// <summary>
/// What the mobile Calendar screen needs to decide between "Connect" and "Connected as ..." UI.
/// <paramref name="TasksConnected"/> is false for an account connected before Google Tasks existed (or one that declined the Tasks
/// permission): the app then offers to reconnect.
/// </summary>
public record GoogleCalendarStatusDto(
    bool Connected, string? Email, DateTime? LastSyncedAtUtc, bool TasksConnected = false, DateTime? LastTasksSyncedAtUtc = null);
