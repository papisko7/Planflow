using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Domain.Services;

public enum AllDayMode { Optional, Never, Always }

public enum BusyMode
{
    /// <summary>The user chooses Busy or Free.</summary>
    Choice,
    /// <summary>Always Busy, not editable (Focus time).</summary>
    AlwaysBusy,
    /// <summary>The field does not exist for this template.</summary>
    NotApplicable
}

public enum LocationMode { None, Text, WorkingLocation }

/// <summary>What one template allows. The single source for both the backend validation and the mobile form (which hides fields).</summary>
public sealed record TemplateRules(
    string? DefaultTitle,
    AllDayMode AllDay,
    bool AllowsGuests,
    LocationMode Location,
    bool AllowsAlerts,
    bool AllowsColor,
    bool AllowsDescription,
    BusyMode Busy,
    ScoringProfile Profile);

/// <summary>
/// Per-template field configuration (plan point 4) and the scoring profile of an item (point 5). One table in one place,
/// so adding a template means adding one row, not editing validators and screens separately.
/// </summary>
public static class EventTemplateRules
{
    private static readonly TemplateRules TaskRules = new(
        null, AllDayMode.Optional, AllowsGuests: false, LocationMode.None, AllowsAlerts: true, AllowsColor: true,
        AllowsDescription: true, BusyMode.NotApplicable, ScoringProfile.Full);

    private static readonly IReadOnlyDictionary<EventTemplate, TemplateRules> EventRules = new Dictionary<EventTemplate, TemplateRules>
    {
        [EventTemplate.Default] = new(
            null, AllDayMode.Optional, AllowsGuests: true, LocationMode.Text, AllowsAlerts: true, AllowsColor: true,
            AllowsDescription: true, BusyMode.Choice, ScoringProfile.Full),
        [EventTemplate.OutOfOffice] = new(
            "Out of office", AllDayMode.Optional, AllowsGuests: false, LocationMode.None, AllowsAlerts: false, AllowsColor: true,
            AllowsDescription: false, BusyMode.NotApplicable, ScoringProfile.Informational),
        [EventTemplate.FocusTime] = new(
            "Focus time", AllDayMode.Never, AllowsGuests: false, LocationMode.Text, AllowsAlerts: true, AllowsColor: true,
            AllowsDescription: true, BusyMode.AlwaysBusy, ScoringProfile.Informational),
        [EventTemplate.WorkingLocation] = new(
            "Working location", AllDayMode.Optional, AllowsGuests: false, LocationMode.WorkingLocation, AllowsAlerts: false, AllowsColor: false,
            AllowsDescription: false, BusyMode.NotApplicable, ScoringProfile.Informational),
        [EventTemplate.Birthday] = new(
            null, AllDayMode.Always, AllowsGuests: false, LocationMode.None, AllowsAlerts: true, AllowsColor: true,
            AllowsDescription: false, BusyMode.NotApplicable, ScoringProfile.Informational)
    };

    /// <summary>A Task always uses the task rules; only an Event can use a non-default template.</summary>
    public static TemplateRules For(TaskType type, EventTemplate template) =>
        type == TaskType.Task ? TaskRules : EventRules[template];

    public static ScoringProfile ProfileOf(CalendarItem item) => For(item.TaskType, item.EventTemplate).Profile;

    /// <summary>
    /// Google's <c>eventType</c> as a template. "fromGmail" (flights, hotels, reservations) and anything unknown or new
    /// is an ordinary event, so a future Google event type can never break the import.
    /// </summary>
    public static EventTemplate FromGoogleEventType(string? eventType) => eventType switch
    {
        "outOfOffice" => EventTemplate.OutOfOffice,
        "focusTime" => EventTemplate.FocusTime,
        "workingLocation" => EventTemplate.WorkingLocation,
        "birthday" => EventTemplate.Birthday,
        _ => EventTemplate.Default
    };

    /// <summary>A Task must use the Default template, and the value must be a known template.</summary>
    public static bool IsValid(TaskType type, EventTemplate template) =>
        Enum.IsDefined(template) && (type == TaskType.Event || template == EventTemplate.Default);
}
