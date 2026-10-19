using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;

namespace PlanFlow.Application.Availability.Common;

/// <summary>One local day of one person: how free they are and how loaded with work.</summary>
public record DayAvailabilityDto(
    DateTime Date, DayAvailability Availability, double CapacityHours, double LoadHours, double? LoadPercent, LoadLevel LoadLevel);

/// <summary>
/// A teammate as the availability grid and the assignee pickers see them. <see cref="CurrentStatus"/> is "right now";
/// <see cref="WindowLoadPercent"/>/<see cref="LoadLevel"/> cover the requested days. <see cref="CurrentWorkingLocation"/> is filled
/// only when the person shares details. <see cref="TasksWithoutEffort"/> are open tasks the load cannot count (no effort estimate).
/// </summary>
public record MemberAvailabilityDto(
    Guid UserId,
    string DisplayName,
    TeamRole Role,
    CalendarSharingLevel SharingLevel,
    MemberStatus CurrentStatus,
    string? CurrentWorkingLocation,
    double? WindowLoadPercent,
    LoadLevel LoadLevel,
    int OpenTasks,
    int TasksWithoutEffort,
    IReadOnlyList<DayAvailabilityDto> Days);

public record TeamAvailabilityDto(Guid TeamId, DateTime From, int Days, DateTime NowUtc, IReadOnlyList<MemberAvailabilityDto> Members);

/// <summary>One block of a member's calendar. <see cref="Source"/> is "Team" (a team event, shown in full) or "Personal" (imported, trimmed by the sharing level).</summary>
public record MemberCalendarEntryDto(
    Guid ItemId, string Source, BlockKind Kind, DateTime StartUtc, DateTime EndUtc, bool IsAllDay, string? Title, string? Location);

/// <summary>An open task of the member in this team, with the deadline that loads their time.</summary>
public record MemberTaskDto(
    Guid ItemId, string Title, DateTime DeadlineUtc, bool IsAllDay, double? EffortHours, Domain.Enums.TaskStatus Status, double UrgencyScore);

/// <summary>
/// <see cref="Tasks"/> are the member's open tasks; <see cref="FinishedTasks"/> are their completed tasks of this team whose
/// deadline falls in the requested window (an item keeps no completion time, so a finished task stays on its deadline day).
/// </summary>
public record MemberCalendarDto(
    Guid TeamId,
    MemberAvailabilityDto Member,
    string? TimeZoneId,
    IReadOnlyList<MemberCalendarEntryDto> Entries,
    IReadOnlyList<MemberTaskDto> Tasks,
    IReadOnlyList<MemberTaskDto> FinishedTasks);
