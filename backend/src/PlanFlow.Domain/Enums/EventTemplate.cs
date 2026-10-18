namespace PlanFlow.Domain.Enums;

/// <summary>
/// The Google-Calendar-style template of an item. A Task is always <see cref="Default"/>; an Event can be any of them.
/// Together with <see cref="TaskType"/> it forms the "chip" the user picks in the form.
/// </summary>
public enum EventTemplate
{
    Default = 0,
    OutOfOffice = 1,
    FocusTime = 2,
    WorkingLocation = 3,
    Birthday = 4
}
