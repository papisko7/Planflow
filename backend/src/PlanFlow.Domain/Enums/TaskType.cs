namespace PlanFlow.Domain.Enums;

/// <summary>
/// What a <see cref="Entities.CalendarItem"/> represents. A Task is work to be done (the clock is its deadline minus
/// the effort it needs); an Event is something that happens at a fixed moment (the clock is its start time).
/// </summary>
public enum TaskType
{
    Task = 0,
    Event = 1
}
