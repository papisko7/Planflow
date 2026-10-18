using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;

namespace PlanFlow.Tests.Domain.Services;

/// <summary>When a reminder goes off: offset and day forms, start and deadline anchors, all-day items and the person's own clock.</summary>
public class ReminderScheduleTests
{
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
    private static readonly TimeZoneInfo PagoPago = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Pago_Pago"); // UTC-11, no daylight saving

    private static DateTime Utc(int y, int m, int d, int h = 0, int min = 0) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    private static CalendarItem Event(DateTime start, bool allDay = false) =>
        new() { TaskType = TaskType.Event, StartUtc = start, IsAllDay = allDay };

    private static CalendarItem TaskItem(DateTime? deadline, DateTime? start = null, bool allDay = false) =>
        new() { TaskType = TaskType.Task, DeadlineUtc = deadline, StartUtc = start, IsAllDay = allDay };

    // ---- offset form ----

    [Fact]
    public void Offset_CountsBackFromTheStartOfATimedEvent()
    {
        var trigger = ReminderSchedule.TriggerUtc(Event(Utc(2026, 10, 20, 10)), ItemReminder.Offset(30), TimeZoneInfo.Utc);

        Assert.Equal(Utc(2026, 10, 20, 9, 30), trigger);
    }

    [Fact]
    public void Offset_OfZero_IsTheMomentItself()
    {
        Assert.Equal(Utc(2026, 10, 20, 10), ReminderSchedule.TriggerUtc(Event(Utc(2026, 10, 20, 10)), ItemReminder.Offset(0), Warsaw));
    }

    [Fact]
    public void Offset_FromTheDeadlineOfATask()
    {
        var item = TaskItem(deadline: Utc(2026, 10, 25, 17));

        var trigger = ReminderSchedule.TriggerUtc(item, ItemReminder.Offset(2 * 24 * 60, ReminderAnchor.Deadline), Warsaw);

        Assert.Equal(Utc(2026, 10, 23, 17), trigger);
    }

    [Fact]
    public void Offset_FromThePlannedStartOfATask()
    {
        var item = TaskItem(deadline: Utc(2026, 10, 25, 17), start: Utc(2026, 10, 22, 8));

        Assert.Equal(Utc(2026, 10, 22, 7), ReminderSchedule.TriggerUtc(item, ItemReminder.Offset(60), TimeZoneInfo.Utc));
    }

    [Fact]
    public void AnAnchorTheItemLacks_NeverFires()
    {
        Assert.Null(ReminderSchedule.TriggerUtc(TaskItem(deadline: Utc(2026, 10, 25, 17)), ItemReminder.Offset(10), TimeZoneInfo.Utc)); // a task with no planned start
        Assert.Null(ReminderSchedule.TriggerUtc(TaskItem(deadline: null, start: Utc(2026, 10, 22)), ItemReminder.Offset(10, ReminderAnchor.Deadline), TimeZoneInfo.Utc));
        Assert.Null(ReminderSchedule.TriggerUtc(Event(Utc(2026, 10, 20, 10)), ItemReminder.Offset(10, ReminderAnchor.Deadline), TimeZoneInfo.Utc)); // an event has no deadline
    }

    // ---- day form ----

    [Fact]
    public void DayForm_IsATimeOfDayOnThePersonsOwnClock()
    {
        // The event starts at 10:00 in Warsaw (UTC+2 in October); "1 day before at 09:00" is 09:00 Warsaw = 07:00 UTC the day before.
        var trigger = ReminderSchedule.TriggerUtc(Event(Utc(2026, 10, 20, 8)), ItemReminder.Day(1, 9 * 60), Warsaw);

        Assert.Equal(Utc(2026, 10, 19, 7), trigger);
    }

    [Fact]
    public void DayForm_UsesTheDayOnThePersonsClock_NotTheDayInUtc()
    {
        // 22:30 UTC on the 20th is already 00:30 on the 21st in Warsaw, so "1 day before" is the 20th (09:00 Warsaw = 07:00 UTC).
        var trigger = ReminderSchedule.TriggerUtc(Event(Utc(2026, 10, 20, 22, 30)), ItemReminder.Day(1, 9 * 60), Warsaw);

        Assert.Equal(Utc(2026, 10, 20, 7), trigger);
    }

    [Fact]
    public void DayForm_ATimeLaterThanTheEventItself_NeverFires()
    {
        // "The same day at 23:00" for a 10:00 meeting is after it.
        Assert.Null(ReminderSchedule.TriggerUtc(Event(Utc(2026, 10, 20, 8)), ItemReminder.Day(0, 23 * 60), Warsaw));
    }

    [Fact]
    public void DayForm_ATimeThatDoesNotExistOnADaylightSavingDay_IsTheFirstMomentThatDoes()
    {
        // On 29 March 2026 the Warsaw clock jumps from 02:00 to 03:00, so 02:30 never happens; the reminder goes off at 03:30 (01:30 UTC).
        var trigger = ReminderSchedule.TriggerUtc(Event(Utc(2026, 3, 29, 12)), ItemReminder.Day(0, 2 * 60 + 30), Warsaw);

        Assert.Equal(Utc(2026, 3, 29, 1, 30), trigger);
    }

    // ---- all-day items ----

    [Fact]
    public void AllDay_DayForm_ReadsTheStoredDayLiterally_AndTheTimeOnThePersonsClock()
    {
        var holiday = Event(Utc(2026, 10, 20), allDay: true); // stored as midnight UTC of the 20th

        // Warsaw: the day before at 09:00 local = 07:00 UTC. Far west (UTC-11) the same wall-clock time is 20:00 UTC.
        Assert.Equal(Utc(2026, 10, 19, 7), ReminderSchedule.TriggerUtc(holiday, ItemReminder.Day(1, 9 * 60), Warsaw));
        Assert.Equal(Utc(2026, 10, 19, 20), ReminderSchedule.TriggerUtc(holiday, ItemReminder.Day(1, 9 * 60), PagoPago));
    }

    [Fact]
    public void AllDay_TheDayItself_IsStillAhead_AtAnyTimeOfThatDay()
    {
        // Google writes "the day itself at 09:00" for an all-day event, which is after its midnight start but within the day.
        var holiday = Event(Utc(2026, 10, 20), allDay: true);

        Assert.Equal(Utc(2026, 10, 20, 7), ReminderSchedule.TriggerUtc(holiday, ItemReminder.Day(0, 9 * 60), Warsaw));
    }

    [Fact]
    public void AllDay_TaskDeadline_CountsFromTheEndOfThatDay()
    {
        // Due on the 22nd: the deadline moment is the end of the 22nd in Warsaw (= 22:00 UTC, the midnight of the 23rd local).
        var task = TaskItem(deadline: Utc(2026, 10, 22), allDay: true);

        Assert.Equal(Utc(2026, 10, 22, 20), ReminderSchedule.TriggerUtc(task, ItemReminder.Offset(2 * 60, ReminderAnchor.Deadline), Warsaw));
        Assert.Equal(Utc(2026, 10, 21, 16), ReminderSchedule.TriggerUtc(task, ItemReminder.Day(1, 18 * 60, ReminderAnchor.Deadline), Warsaw));
    }

    // ---- due ----

    [Fact]
    public void IsDue_FromTheMomentOn_UntilTheGraceRunsOut()
    {
        var trigger = Utc(2026, 10, 20, 9, 30);

        Assert.False(ReminderSchedule.IsDue(trigger, null, trigger.AddSeconds(-1)));
        Assert.True(ReminderSchedule.IsDue(trigger, null, trigger));
        Assert.True(ReminderSchedule.IsDue(trigger, null, trigger.AddMinutes(9)));
        Assert.False(ReminderSchedule.IsDue(trigger, null, trigger.AddMinutes(11))); // long gone: never caught up
    }

    [Fact]
    public void IsDue_AReminderThatAlreadyWentOffForThatMoment_DoesNotRepeat()
    {
        var trigger = Utc(2026, 10, 20, 9, 30);

        Assert.False(ReminderSchedule.IsDue(trigger, trigger, trigger.AddMinutes(1)));
        Assert.True(ReminderSchedule.IsDue(trigger, trigger.AddHours(-1), trigger.AddMinutes(1))); // it went off for an earlier moment: the item moved
    }

    [Fact]
    public void IsDue_NoMoment_IsNeverDue()
    {
        Assert.False(ReminderSchedule.IsDue(null, null, Utc(2026, 10, 20)));
    }
}
