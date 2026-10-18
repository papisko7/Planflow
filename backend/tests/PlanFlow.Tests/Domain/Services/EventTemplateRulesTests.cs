using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;

namespace PlanFlow.Tests.Domain.Services;

public class EventTemplateRulesTests
{
    [Theory]
    [InlineData(EventTemplate.OutOfOffice)]
    [InlineData(EventTemplate.FocusTime)]
    [InlineData(EventTemplate.WorkingLocation)]
    [InlineData(EventTemplate.Birthday)]
    public void InformationalTemplates_AreOutsideTheRanking(EventTemplate template) =>
        Assert.Equal(ScoringProfile.Informational, EventTemplateRules.For(TaskType.Event, template).Profile);

    [Fact]
    public void DefaultEventAndTask_UseTheFullProfile()
    {
        Assert.Equal(ScoringProfile.Full, EventTemplateRules.For(TaskType.Event, EventTemplate.Default).Profile);
        Assert.Equal(ScoringProfile.Full, EventTemplateRules.For(TaskType.Task, EventTemplate.Default).Profile);
    }

    [Fact]
    public void ProfileOf_ReadsTheItemsTypeAndTemplate()
    {
        var vacation = new CalendarItem { TaskType = TaskType.Event, EventTemplate = EventTemplate.OutOfOffice };
        var meeting = new CalendarItem { TaskType = TaskType.Event };
        var task = new CalendarItem();

        Assert.Equal(ScoringProfile.Informational, EventTemplateRules.ProfileOf(vacation));
        Assert.Equal(ScoringProfile.Full, EventTemplateRules.ProfileOf(meeting));
        Assert.Equal(ScoringProfile.Full, EventTemplateRules.ProfileOf(task));
    }

    [Fact]
    public void ATask_IgnoresTheTemplateAndAlwaysUsesTheTaskRules()
    {
        var rules = EventTemplateRules.For(TaskType.Task, EventTemplate.OutOfOffice);

        Assert.Equal(ScoringProfile.Full, rules.Profile);
        Assert.True(rules.AllowsDescription);
    }

    [Theory]
    [InlineData(TaskType.Task, EventTemplate.Default, true)]
    [InlineData(TaskType.Task, EventTemplate.FocusTime, false)] // only an event can use a special template
    [InlineData(TaskType.Event, EventTemplate.FocusTime, true)]
    [InlineData(TaskType.Event, (EventTemplate)99, false)]
    public void IsValid_TaskNeedsDefault_UnknownTemplateIsRejected(TaskType type, EventTemplate template, bool expected) =>
        Assert.Equal(expected, EventTemplateRules.IsValid(type, template));

    [Theory]
    [InlineData("default", EventTemplate.Default)]
    [InlineData("outOfOffice", EventTemplate.OutOfOffice)]
    [InlineData("focusTime", EventTemplate.FocusTime)]
    [InlineData("workingLocation", EventTemplate.WorkingLocation)]
    [InlineData("birthday", EventTemplate.Birthday)]
    [InlineData("fromGmail", EventTemplate.Default)]        // flights, reservations: ordinary events
    [InlineData("someFutureGoogleType", EventTemplate.Default)] // a new Google type never breaks the import
    [InlineData(null, EventTemplate.Default)]
    public void FromGoogleEventType_MapsKnownTypesAndFallsBackToDefault(string? googleType, EventTemplate expected) =>
        Assert.Equal(expected, EventTemplateRules.FromGoogleEventType(googleType));

    [Fact]
    public void EveryTemplateHasARow()
    {
        foreach (var template in Enum.GetValues<EventTemplate>())
        {
            Assert.NotNull(EventTemplateRules.For(TaskType.Event, template));
        }
    }

    [Fact]
    public void TemplateFields_FollowThePlanTable()
    {
        var focus = EventTemplateRules.For(TaskType.Event, EventTemplate.FocusTime);
        Assert.Equal("Focus time", focus.DefaultTitle);
        Assert.Equal(AllDayMode.Never, focus.AllDay);
        Assert.Equal(BusyMode.AlwaysBusy, focus.Busy);

        var birthday = EventTemplateRules.For(TaskType.Event, EventTemplate.Birthday);
        Assert.Equal(AllDayMode.Always, birthday.AllDay);
        Assert.False(birthday.AllowsGuests);

        var location = EventTemplateRules.For(TaskType.Event, EventTemplate.WorkingLocation);
        Assert.Equal(LocationMode.WorkingLocation, location.Location);
        Assert.False(location.AllowsColor);

        var meeting = EventTemplateRules.For(TaskType.Event, EventTemplate.Default);
        Assert.True(meeting.AllowsGuests);
        Assert.Equal(BusyMode.Choice, meeting.Busy);
    }
}
