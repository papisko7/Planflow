using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;

namespace PlanFlow.Tests.Domain.Services;

/// <summary>
/// What a teammate may see of a personal calendar (sharing levels, privacy), how available a person is (status now and per day)
/// and how loaded with work they are (capacity 8 h on weekdays, effort spread over the days up to the deadline).
/// </summary>
public class MemberCalendarServicesTests
{
    // 2026-10-19 is a Monday.
    private static readonly DateTime Monday = new(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static CalendarItem Event(
        string title = "Therapy", int startHour = 10, int hours = 1, bool? isPrivate = false, EventTemplate template = EventTemplate.Default,
        bool isBusy = true, string? location = "Clinic") =>
        new()
        {
            Title = title, TaskType = TaskType.Event, EventTemplate = template, IsBusy = isBusy, IsPrivate = isPrivate, Location = location,
            StartUtc = Monday.AddHours(startHour), EndUtc = Monday.AddHours(startHour + hours)
        };

    private static CalendarItem AllDay(EventTemplate template, int days = 1, bool? isPrivate = false) =>
        new()
        {
            Title = "Holiday", TaskType = TaskType.Event, EventTemplate = template, IsBusy = true, IsPrivate = isPrivate, IsAllDay = true,
            StartUtc = Monday, EndUtc = Monday.AddDays(days)
        };

    // ----- PersonalCalendarSharing -----

    [Fact]
    public void Sharing_Off_ShowsNothing() =>
        Assert.Null(PersonalCalendarSharing.BlockOf(Event(), CalendarSharingLevel.Off));

    [Fact]
    public void Sharing_Availability_KeepsTimeAndKindButNoText()
    {
        var block = PersonalCalendarSharing.BlockOf(Event(template: EventTemplate.FocusTime), CalendarSharingLevel.Availability)!;

        Assert.Equal(BlockKind.FocusTime, block.Kind);
        Assert.Equal(Monday.AddHours(10), block.StartUtc);
        Assert.Null(block.Title);
        Assert.Null(block.Location);
    }

    [Fact]
    public void Sharing_Details_ShowsTitleAndPlace()
    {
        var block = PersonalCalendarSharing.BlockOf(Event(), CalendarSharingLevel.Details)!;

        Assert.Equal("Therapy", block.Title);
        Assert.Equal("Clinic", block.Location);
    }

    [Theory]
    [InlineData(CalendarSharingLevel.Availability)]
    [InlineData(CalendarSharingLevel.Details)]
    public void Sharing_PrivateEvent_IsOnlyEverBusy(CalendarSharingLevel level)
    {
        var block = PersonalCalendarSharing.BlockOf(Event(isPrivate: true, template: EventTemplate.OutOfOffice), level)!;

        Assert.Equal(BlockKind.Busy, block.Kind);
        Assert.Null(block.Title);
        Assert.Null(block.Location);
    }

    [Fact]
    public void Sharing_UnknownPrivacy_IsTreatedAsPrivate()
    {
        var block = PersonalCalendarSharing.BlockOf(Event(isPrivate: null), CalendarSharingLevel.Details)!;

        Assert.Null(block.Title);
    }

    [Fact]
    public void Sharing_FreeEvent_AppearsOnlyWithDetails()
    {
        Assert.Null(PersonalCalendarSharing.BlockOf(Event(isBusy: false), CalendarSharingLevel.Availability));
        Assert.Equal(BlockKind.Free, PersonalCalendarSharing.BlockOf(Event(isBusy: false), CalendarSharingLevel.Details)!.Kind);
    }

    [Fact]
    public void Sharing_EventWithoutEnd_GetsDefaultLength()
    {
        var item = Event();
        item.EndUtc = null;

        var block = PersonalCalendarSharing.BlockOf(item, CalendarSharingLevel.Availability)!;

        Assert.Equal(item.StartUtc!.Value.AddHours(1), block.EndUtc);
    }

    // ----- AvailabilityService -----

    [Fact]
    public void Status_OffMeansNoData_NotFree() =>
        Assert.Equal(MemberStatus.NoData, AvailabilityService.StatusAt(CalendarSharingLevel.Off, [], Utc, Monday.AddHours(10)));

    [Fact]
    public void Status_NoBlocks_IsFree() =>
        Assert.Equal(MemberStatus.Free, AvailabilityService.StatusAt(CalendarSharingLevel.Availability, [], Utc, Monday.AddHours(10)));

    [Fact]
    public void Status_OutOfOfficeBeatsMeetingBeatsFocus()
    {
        var blocks = new[]
        {
            PersonalCalendarSharing.BlockOf(Event(template: EventTemplate.FocusTime), CalendarSharingLevel.Availability)!,
            PersonalCalendarSharing.BlockOf(Event(), CalendarSharingLevel.Availability)!
        };
        var at = Monday.AddHours(10).AddMinutes(30);

        Assert.Equal(MemberStatus.Busy, AvailabilityService.StatusAt(CalendarSharingLevel.Availability, blocks, Utc, at));

        var withAbsence = blocks.Append(PersonalCalendarSharing.BlockOf(AllDay(EventTemplate.OutOfOffice), CalendarSharingLevel.Availability)!);
        Assert.Equal(MemberStatus.OutOfOffice, AvailabilityService.StatusAt(CalendarSharingLevel.Availability, withAbsence, Utc, at));
    }

    [Fact]
    public void Status_EndOfBlockIsExclusive()
    {
        var blocks = new[] { PersonalCalendarSharing.BlockOf(Event(), CalendarSharingLevel.Availability)! };

        Assert.Equal(MemberStatus.Busy, AvailabilityService.StatusAt(CalendarSharingLevel.Availability, blocks, Utc, Monday.AddHours(10)));
        Assert.Equal(MemberStatus.Free, AvailabilityService.StatusAt(CalendarSharingLevel.Availability, blocks, Utc, Monday.AddHours(11)));
    }

    [Fact]
    public void Status_WorkingLocationAndBirthday_DoNotMakeAPersonBusy()
    {
        var blocks = new[]
        {
            PersonalCalendarSharing.BlockOf(AllDay(EventTemplate.WorkingLocation), CalendarSharingLevel.Availability)!,
            PersonalCalendarSharing.BlockOf(AllDay(EventTemplate.Birthday), CalendarSharingLevel.Availability)!
        };

        Assert.Equal(MemberStatus.Free, AvailabilityService.StatusAt(CalendarSharingLevel.Availability, blocks, Utc, Monday.AddHours(10)));
        Assert.Equal(DayAvailability.Free, AvailabilityService.DayStatus(CalendarSharingLevel.Availability, blocks, Utc, Monday));
    }

    [Fact]
    public void Status_AllDayAbsence_IsReadInThePersonsZone()
    {
        // 20 Oct 00:00 in Warsaw (UTC+2) is 19 Oct 22:00 UTC, so the absence already covers 19 Oct 23:00 UTC.
        var warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
        var item = AllDay(EventTemplate.OutOfOffice);
        item.StartUtc = Monday.AddDays(1);
        item.EndUtc = Monday.AddDays(2);
        var blocks = new[] { PersonalCalendarSharing.BlockOf(item, CalendarSharingLevel.Availability)! };

        Assert.Equal(MemberStatus.OutOfOffice, AvailabilityService.StatusAt(CalendarSharingLevel.Availability, blocks, warsaw, Monday.AddHours(23)));
        Assert.Equal(MemberStatus.Free, AvailabilityService.StatusAt(CalendarSharingLevel.Availability, blocks, Utc, Monday.AddHours(23)));
    }

    [Fact]
    public void Day_Verdicts_FollowTheThresholds()
    {
        Assert.Equal(DayAvailability.NoData, AvailabilityService.DayStatus(CalendarSharingLevel.Off, [], Utc, Monday));
        Assert.Equal(DayAvailability.Free, AvailabilityService.DayStatus(CalendarSharingLevel.Availability, [], Utc, Monday));

        TimeBlock Meeting(int startHour, int hours) =>
            PersonalCalendarSharing.BlockOf(Event(startHour: startHour, hours: hours), CalendarSharingLevel.Availability)!;

        Assert.Equal(DayAvailability.Partial, AvailabilityService.DayStatus(CalendarSharingLevel.Availability, [Meeting(9, 2)], Utc, Monday));
        Assert.Equal(DayAvailability.Busy, AvailabilityService.DayStatus(CalendarSharingLevel.Availability, [Meeting(9, 7)], Utc, Monday));
    }

    [Fact]
    public void Day_OverlappingMeetings_AreCountedOnce()
    {
        TimeBlock Meeting(int startHour, int hours) =>
            PersonalCalendarSharing.BlockOf(Event(startHour: startHour, hours: hours), CalendarSharingLevel.Availability)!;

        // 9-13 and 10-14 cover 5 hours together, not 8.
        var verdict = AvailabilityService.DayStatus(CalendarSharingLevel.Availability, [Meeting(9, 4), Meeting(10, 4)], Utc, Monday);

        Assert.Equal(DayAvailability.Partial, verdict);
    }

    [Fact]
    public void Day_HalfADayOutOfOffice_IsAnAbsenceDay()
    {
        var absence = PersonalCalendarSharing.BlockOf(Event(startHour: 8, hours: 4, template: EventTemplate.OutOfOffice), CalendarSharingLevel.Availability)!;

        Assert.Equal(DayAvailability.OutOfOffice, AvailabilityService.DayStatus(CalendarSharingLevel.Availability, [absence], Utc, Monday));
    }

    // ----- WorkloadService -----

    [Fact]
    public void Workload_WeekendHasNoCapacity()
    {
        var days = WorkloadService.Compute(Monday.AddDays(5), 2, Utc, Monday, [], []);

        Assert.All(days, d => Assert.Equal(0, d.CapacityHours));
        Assert.All(days, d => Assert.Null(d.Percent));
    }

    [Fact]
    public void Workload_EffortIsSpreadOverTheWorkingDaysUpToTheDeadline()
    {
        // 12 h due end of Wednesday, today Monday: 3 working days of 8 h, so 4 h a day = 50 %.
        var task = new WorkTask(Guid.NewGuid(), null, Monday.AddDays(3).AddTicks(-1), 12);

        var days = WorkloadService.Compute(Monday, 5, Utc, Monday.AddHours(8), [], [task]);

        Assert.Equal(4, days[0].LoadHours, 6);
        Assert.Equal(4, days[2].LoadHours, 6);
        Assert.Equal(0, days[3].LoadHours);
        Assert.Equal(50, days[0].Percent!.Value, 6);
        Assert.Equal(50, WorkloadService.WindowPercent(days.Take(3).ToList())!.Value, 6);
    }

    [Fact]
    public void Workload_OverdueTaskWeighsOnToday()
    {
        var task = new WorkTask(Guid.NewGuid(), null, Monday.AddDays(-3), 6);

        var days = WorkloadService.Compute(Monday, 3, Utc, Monday.AddHours(8), [], [task]);

        Assert.Equal(6, days[0].LoadHours, 6);
        Assert.Equal(0, days[1].LoadHours);
    }

    [Fact]
    public void Workload_AbsenceRemovesCapacityAndMovesWorkToOtherDays()
    {
        var absence = PersonalCalendarSharing.BlockOf(AllDay(EventTemplate.OutOfOffice), CalendarSharingLevel.Availability)!;
        var task = new WorkTask(Guid.NewGuid(), null, Monday.AddDays(2).AddTicks(-1), 8); // due end of Tuesday

        var days = WorkloadService.Compute(Monday, 2, Utc, Monday.AddHours(8), [absence], [task]);

        Assert.Equal(0, days[0].CapacityHours);
        Assert.Equal(0, days[0].LoadHours);
        Assert.Equal(8, days[1].LoadHours, 6);
        Assert.Equal(100, days[1].Percent!.Value, 6);
    }

    [Fact]
    public void Workload_FocusTimeDoesNotReduceCapacity_MeetingsDo()
    {
        var focus = PersonalCalendarSharing.BlockOf(Event(startHour: 9, hours: 4, template: EventTemplate.FocusTime), CalendarSharingLevel.Availability)!;
        var meeting = PersonalCalendarSharing.BlockOf(Event(startHour: 9, hours: 4), CalendarSharingLevel.Availability)!;

        Assert.Equal(8, WorkloadService.Compute(Monday, 1, Utc, Monday.AddHours(8), [focus], [])[0].CapacityHours);
        Assert.Equal(4, WorkloadService.Compute(Monday, 1, Utc, Monday.AddHours(8), [meeting], [])[0].CapacityHours);
    }

    [Fact]
    public void Workload_WorkWithNoCapacity_ShowsAsOverloaded()
    {
        var absence = PersonalCalendarSharing.BlockOf(AllDay(EventTemplate.OutOfOffice, days: 7), CalendarSharingLevel.Availability)!;
        var task = new WorkTask(Guid.NewGuid(), null, Monday.AddDays(3), 2);

        var days = WorkloadService.Compute(Monday, 4, Utc, Monday.AddHours(8), [absence], [task]);

        Assert.Equal(WorkloadService.NoCapacityPercent, WorkloadService.WindowPercent(days));
        Assert.Equal(LoadLevel.Overloaded, WorkloadService.LevelOf(WorkloadService.WindowPercent(days)));
    }

    [Theory]
    [InlineData(null, LoadLevel.Light)]
    [InlineData(10.0, LoadLevel.Light)]
    [InlineData(59.9, LoadLevel.Light)]
    [InlineData(60.0, LoadLevel.Balanced)]
    [InlineData(100.0, LoadLevel.Balanced)]
    [InlineData(100.1, LoadLevel.Overloaded)]
    public void Workload_Levels_FollowTheThresholds(double? percent, LoadLevel expected) =>
        Assert.Equal(expected, WorkloadService.LevelOf(percent));

    [Fact]
    public void Workload_AllDayDeadline_IsTheEndOfThatDayInThePersonsZone()
    {
        var warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

        var deadline = WorkloadService.RealDeadlineUtc(Monday, isAllDay: true, warsaw);

        // End of Monday 19 Oct in Warsaw (UTC+2) is 20 Oct 00:00 local = 19 Oct 22:00 UTC, minus one tick.
        Assert.Equal(Monday.AddHours(22).AddTicks(-1), deadline);
        Assert.Equal(Monday, WorkloadService.RealDeadlineUtc(Monday, isAllDay: false, warsaw));
    }
}
