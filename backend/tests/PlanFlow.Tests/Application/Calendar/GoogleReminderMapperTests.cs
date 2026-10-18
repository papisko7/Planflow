using PlanFlow.Application.Calendar.Common;
using PlanFlow.Domain.Entities;
using Xunit;

namespace PlanFlow.Tests.Application.Calendar;

public class GoogleReminderMapperTests
{
    [Fact]
    public void TimedEvent_KeepsTheOffsetInMinutes()
    {
        var reminders = GoogleReminderMapper.Map([10, 60, 1440], isAllDay: false);

        Assert.Equal([10, 60, 1440], reminders.Select(r => r.OffsetMinutes!.Value));
        Assert.All(reminders, r => Assert.Null(r.DaysBefore));
    }

    [Theory]
    [InlineData(900, 1, 540)]        // 15 h before midnight = the day before at 09:00
    [InlineData(1440, 1, 0)]         // exactly a day before, at midnight
    [InlineData(0, 0, 0)]            // the day itself at 00:00
    [InlineData(540, 1, 900)]        // 9 h before midnight = the day before at 15:00
    [InlineData(10080, 7, 0)]        // a week before
    [InlineData(-540, 0, 540)]       // negative = later than midnight of the day itself: the day itself at 09:00
    public void AllDayEvent_BecomesDaysBeforeAtATimeOfDay(int googleMinutes, int expectedDays, int expectedTimeOfDay)
    {
        var reminder = Assert.Single(GoogleReminderMapper.Map([googleMinutes], isAllDay: true));

        Assert.Equal(expectedDays, reminder.DaysBefore);
        Assert.Equal(expectedTimeOfDay, reminder.TimeOfDayMinutes);
        Assert.Null(reminder.OffsetMinutes);
    }

    [Fact]
    public void ReminderThatCannotBeExpressed_IsSkippedNotAnError()
    {
        // A timed reminder cannot be negative; an all-day one cannot be more than a day later than midnight of its day.
        Assert.Empty(GoogleReminderMapper.Map([-5], isAllDay: false));
        Assert.Empty(GoogleReminderMapper.Map([-2000], isAllDay: true));
    }

    [Fact]
    public void SameMinutesFromTwoMethods_BecomeOneReminder()
    {
        // Google can send a pop-up and an e-mail reminder for the same moment; both arrive as plain minutes.
        var reminders = GoogleReminderMapper.Map([30, 30, 60], isAllDay: false);

        Assert.Equal([30, 60], reminders.Select(r => r.OffsetMinutes!.Value));
    }

    [Fact]
    public void MoreThanFour_KeepsTheFirstFourInGooglesOrder()
    {
        var reminders = GoogleReminderMapper.Map([5, 10, 15, 20, 25, 30], isAllDay: false);

        Assert.Equal(ItemReminder.MaxPerItem, reminders.Count);
        Assert.Equal([5, 10, 15, 20], reminders.Select(r => r.OffsetMinutes!.Value));
    }

    [Fact]
    public void NoReminders_GiveAnEmptyList() =>
        Assert.Empty(GoogleReminderMapper.Map([], isAllDay: false));
}
