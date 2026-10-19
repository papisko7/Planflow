using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;

namespace PlanFlow.Tests.Domain.Services;

/// <summary>The arithmetic of a repeat rule - which moments are its occurrences.</summary>
public class RecurrenceRulesTests
{
    private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static RecurrenceSeries Rule(
        RecurrenceFrequency frequency, int interval, DateTime anchor, int weekdays = 0, DateTime? ends = null, string? zone = null) =>
        new() { Frequency = frequency, Interval = interval, AnchorUtc = anchor, Weekdays = weekdays, EndsAtUtc = ends, TimeZoneId = zone };

    private static List<DateTime> Take(RecurrenceSeries rule, int count, bool allDay = false) =>
        RecurrenceRules.Occurrences(rule, allDay, DateTime.MinValue.AddYears(1), DateTime.MaxValue.AddYears(-1)).Take(count).ToList();

    [Fact]
    public void Daily_EveryTwoDays_StartsAtTheAnchor()
    {
        var rule = Rule(RecurrenceFrequency.Daily, 2, Utc(2026, 10, 19, 9));

        Assert.Equal([Utc(2026, 10, 19, 9), Utc(2026, 10, 21, 9), Utc(2026, 10, 23, 9)], Take(rule, 3));
    }

    [Fact]
    public void Weekly_OnMondayAndWednesday_ListsBothDaysOfEveryWeek()
    {
        var monday = Utc(2026, 10, 19, 10);
        var rule = Rule(RecurrenceFrequency.Weekly, 1, monday, weekdays: RecurrenceRules.Weekdays(DayOfWeek.Monday) | RecurrenceRules.Weekdays(DayOfWeek.Wednesday));

        Assert.Equal([Utc(2026, 10, 19, 10), Utc(2026, 10, 21, 10), Utc(2026, 10, 26, 10), Utc(2026, 10, 28, 10)], Take(rule, 4));
    }

    [Fact]
    public void Weekly_EveryTwoWeeks_SkipsTheWeeksBetween()
    {
        var rule = Rule(RecurrenceFrequency.Weekly, 2, Utc(2026, 10, 19, 10));

        Assert.Equal([Utc(2026, 10, 19, 10), Utc(2026, 11, 2, 10), Utc(2026, 11, 16, 10)], Take(rule, 3));
    }

    [Fact]
    public void Weekly_WithNoDaysChosen_UsesTheWeekdayOfTheAnchor()
    {
        var thursday = Utc(2026, 10, 22, 8);
        var rule = Rule(RecurrenceFrequency.Weekly, 1, thursday);

        Assert.Equal([thursday, Utc(2026, 10, 29, 8)], Take(rule, 2));
        Assert.Equal(RecurrenceRules.Weekdays(DayOfWeek.Thursday), RecurrenceRules.EffectiveWeekdays(rule, false));
    }

    [Fact]
    public void Weekly_DaysBeforeTheAnchorInItsOwnWeek_AreNotOccurrences()
    {
        // The anchor is a Wednesday; Monday of that week is earlier than the series itself.
        var rule = Rule(RecurrenceFrequency.Weekly, 1, Utc(2026, 10, 21, 10), weekdays: RecurrenceRules.Weekdays(DayOfWeek.Monday) | RecurrenceRules.Weekdays(DayOfWeek.Wednesday));

        Assert.Equal([Utc(2026, 10, 21, 10), Utc(2026, 10, 26, 10)], Take(rule, 2));
    }

    [Fact]
    public void Monthly_OnThe31st_FallsOnTheLastDayOfShorterMonths()
    {
        var rule = Rule(RecurrenceFrequency.Monthly, 1, Utc(2027, 1, 31, 12));

        Assert.Equal([Utc(2027, 1, 31, 12), Utc(2027, 2, 28, 12), Utc(2027, 3, 31, 12), Utc(2027, 4, 30, 12)], Take(rule, 4));
    }

    [Fact]
    public void Yearly_On29February_FallsOn28thInYearsWithoutIt()
    {
        var rule = Rule(RecurrenceFrequency.Yearly, 1, Utc(2028, 2, 29), zone: null);

        Assert.Equal([Utc(2028, 2, 29), Utc(2029, 2, 28), Utc(2030, 2, 28), Utc(2031, 2, 28), Utc(2032, 2, 29)], Take(rule, 5, allDay: true));
    }

    [Fact]
    public void Occurrences_StopAtTheEnd_WhichItselfCounts()
    {
        var rule = Rule(RecurrenceFrequency.Daily, 1, Utc(2026, 10, 19, 9), ends: Utc(2026, 10, 21, 9));

        Assert.Equal(3, Take(rule, 10).Count);
        Assert.Equal(Utc(2026, 10, 21, 9), Take(rule, 10).Last());
    }

    [Fact]
    public void Occurrences_AfterIsExclusive_ThroughIsInclusive()
    {
        var rule = Rule(RecurrenceFrequency.Daily, 1, Utc(2026, 10, 19, 9));

        var found = RecurrenceRules.Occurrences(rule, false, Utc(2026, 10, 20, 9), Utc(2026, 10, 22, 9)).ToList();

        Assert.Equal([Utc(2026, 10, 21, 9), Utc(2026, 10, 22, 9)], found);
    }

    [Fact]
    public void NextAfter_ReturnsTheFirstLaterOccurrence_AndNullOnceTheSeriesHasEnded()
    {
        var rule = Rule(RecurrenceFrequency.Weekly, 1, Utc(2026, 10, 19, 9), ends: Utc(2026, 11, 2, 9));

        Assert.Equal(Utc(2026, 10, 26, 9), RecurrenceRules.NextAfter(rule, false, Utc(2026, 10, 20)));
        Assert.Null(RecurrenceRules.NextAfter(rule, false, Utc(2026, 11, 2, 9)));
    }

    [Fact]
    public void ADailyMeeting_KeepsItsClockTimeWhenTheClocksChange()
    {
        // Warsaw: summer time (UTC+2) ends on 25 Oct 2026. 09:00 on the wall clock is 07:00 UTC before it and 08:00 UTC after it.
        var rule = Rule(RecurrenceFrequency.Daily, 1, Utc(2026, 10, 24, 7), zone: "Europe/Warsaw");

        var found = Take(rule, 3);

        Assert.Equal([Utc(2026, 10, 24, 7), Utc(2026, 10, 25, 8), Utc(2026, 10, 26, 8)], found);
    }

    [Fact]
    public void AnOccurrenceInTheHourTheClocksSkip_MovesAnHourOn()
    {
        // Warsaw: 29 Mar 2026 02:00 -> 03:00, so 02:30 does not exist that day.
        var rule = Rule(RecurrenceFrequency.Daily, 1, Utc(2026, 3, 28, 1, 30), zone: "Europe/Warsaw");

        var found = Take(rule, 2);

        Assert.Equal(Utc(2026, 3, 28, 1, 30), found[0]);
        Assert.Equal(Utc(2026, 3, 29, 1, 30), found[1]); // 03:30 summer time = 01:30 UTC
    }

    [Fact]
    public void AnAllDayItem_IsReadInUtc_WhateverTheZone()
    {
        var rule = Rule(RecurrenceFrequency.Daily, 1, Utc(2026, 10, 24), zone: "Pacific/Auckland");

        Assert.Equal([Utc(2026, 10, 24), Utc(2026, 10, 25), Utc(2026, 10, 26)], Take(rule, 3, allDay: true));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(127, true)]
    [InlineData(128, false)]
    [InlineData(-1, false)]
    public void WeekdayMask_MustBeSevenBits(int mask, bool valid) => Assert.Equal(valid, RecurrenceRules.IsValidWeekdayMask(mask));
}
