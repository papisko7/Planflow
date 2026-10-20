using Microsoft.EntityFrameworkCore;
using Moq;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Application.Tasks.Commands.BulkTasks;
using PlanFlow.Application.Tasks.Commands.UpdateTask;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Application.Tasks.Queries.GetTasksByTeam;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Application.Tasks.Commands;

/// <summary>
/// An edit of an imported item is not sent to Google at once: the edit only notes which Google fields changed and when, and the next sync
/// sends them. These tests check what gets noted - and what does not (fields Google has no place for, items Google manages itself).
/// </summary>
public class ImportedItemEditTrackingTests
{
    private static ICacheService Cache => new Mock<ICacheService>().Object;

    private static DateTime Day(int daysFromToday) => DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(daysFromToday), DateTimeKind.Utc);

    private static readonly DateTime Start = DateTime.UtcNow.Date.AddDays(3).AddHours(9);

    private sealed record Setup(PlanFlowDbContext Db, User Owner, Team Team);

    private static async Task<Setup> SetUpAsync()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner));
        return new Setup(db, owner, team);
    }

    private static async Task<CalendarItem> AddGoogleTaskAsync(Setup s, bool defaultedDeadline = false)
    {
        var item = new CalendarItem
        {
            TeamId = s.Team.Id, AssignedUserId = s.Owner.Id, Title = "Google title", Description = "Google notes", TaskType = TaskType.Task,
            Status = TaskStatus.Backlog, IsAllDay = true, StartUtc = Day(0), DeadlineUtc = Day(5), IsDeadlineDefaulted = defaultedDeadline,
            ExternalTaskId = "t1", ExternalTaskList = new ExternalTaskList { CalendarIntegrationId = Guid.NewGuid(), ExternalId = "l1", Title = "Tasks" }
        };
        s.Db.CalendarItems.Add(item);
        await s.Db.SaveChangesAsync();
        return item;
    }

    private static async Task<CalendarItem> AddImportedEventAsync(
        Setup s, EventTemplate template = EventTemplate.Default, string? seriesId = null, int dayOffset = 0, string eventId = "e1")
    {
        var item = new CalendarItem
        {
            TeamId = s.Team.Id, AssignedUserId = s.Owner.Id, Title = "Standup", TaskType = TaskType.Event, EventTemplate = template,
            Status = TaskStatus.Confirmed, StartUtc = Start.AddDays(dayOffset), EndUtc = Start.AddDays(dayOffset).AddHours(1), IsBusy = true,
            ExternalCalendarEventId = eventId, SeriesId = seriesId
        };
        s.Db.CalendarItems.Add(item);
        await s.Db.SaveChangesAsync();
        return item;
    }

    /// <summary>The form of a Google task exactly as stored; <paramref name="edit"/> changes what the person changed.</summary>
    private static Task<TaskDto> SaveTaskAsync(Setup s, CalendarItem item, Func<UpdateTaskCommand, UpdateTaskCommand>? edit = null)
    {
        var form = new UpdateTaskCommand(
            item.Id, item.Title, item.Description, item.Status, item.AssignedUserId, item.DeadlineUtc, item.ImpactScore, null, null, null, s.Owner.Id,
            TaskType.Task, StartUtc: item.StartUtc, EstimatedEffortHours: item.EstimatedEffortHours, IsAllDay: true);
        return new UpdateTaskCommandHandler(s.Db, Cache, new NotificationService(s.Db)).Handle(edit?.Invoke(form) ?? form, CancellationToken.None);
    }

    /// <summary>The form of an imported event exactly as stored; <paramref name="edit"/> changes what the person changed.</summary>
    private static Task<TaskDto> SaveEventAsync(
        Setup s, CalendarItem item, Func<UpdateTaskCommand, UpdateTaskCommand>? edit = null, SeriesScope scope = SeriesScope.This)
    {
        var form = new UpdateTaskCommand(
            item.Id, item.Title, item.Description, item.Status, item.AssignedUserId, item.DeadlineUtc, item.ImpactScore, null, null, null, s.Owner.Id,
            item.TaskType, item.StartUtc, item.EstimatedEffortHours, EndUtc: item.EndUtc, EventTemplate: item.EventTemplate, IsAllDay: item.IsAllDay,
            Location: item.Location, ColorKey: item.ColorKey, IsBusy: item.IsBusy, Scope: scope);
        return new UpdateTaskCommandHandler(s.Db, Cache, new NotificationService(s.Db)).Handle(edit?.Invoke(form) ?? form, CancellationToken.None);
    }

    private static async Task<CalendarItem> ReloadAsync(Setup s, Guid id) => await s.Db.CalendarItems.AsNoTracking().SingleAsync(i => i.Id == id);

    // ---- a Google task ----

    [Fact]
    public async Task GoogleTask_EditedTitleNotesAndDueDay_AreNotedForTheNextSync()
    {
        var s = await SetUpAsync();
        var item = await AddGoogleTaskAsync(s);
        var before = DateTime.UtcNow;

        var dto = await SaveTaskAsync(s, item, f => f with { Title = "My title", Description = "My notes", DeadlineUtc = Day(9) });

        var stored = await ReloadAsync(s, item.Id);
        Assert.Equal(MirroredFields.Title | MirroredFields.Description | MirroredFields.Due, stored.LocalEditedFields);
        Assert.InRange(stored.LocalEditedAtUtc!.Value, before, DateTime.UtcNow);
        Assert.True(dto.HasUnsentChanges);
    }

    [Fact]
    public async Task GoogleTask_MarkedDone_IsNoted_AndReopeningItIsNotedToo()
    {
        var s = await SetUpAsync();
        var item = await AddGoogleTaskAsync(s);

        await SaveTaskAsync(s, item, f => f with { Status = TaskStatus.Completed });
        Assert.Equal(MirroredFields.Completed, (await ReloadAsync(s, item.Id)).LocalEditedFields);

        item = await ReloadAsync(s, item.Id);
        await SaveTaskAsync(s, item, f => f with { Status = TaskStatus.Backlog });
        Assert.Equal(MirroredFields.Completed, (await ReloadAsync(s, item.Id)).LocalEditedFields);   // still waiting: the last state is what counts
    }

    [Fact]
    public async Task GoogleTask_OnlyWhatGoogleHasNoFieldFor_IsChanged_NothingIsNoted()
    {
        var s = await SetUpAsync();
        var item = await AddGoogleTaskAsync(s);

        var dto = await SaveTaskAsync(s, item, f => f with { ImpactScore = 9, EstimatedEffortHours = 3, StartUtc = Day(2), Status = TaskStatus.InProgress });

        var stored = await ReloadAsync(s, item.Id);
        Assert.Equal(MirroredFields.None, stored.LocalEditedFields);
        Assert.Null(stored.LocalEditedAtUtc);
        Assert.False(dto.HasUnsentChanges);
    }

    [Fact]
    public async Task GoogleTask_PlaceholderDeadlineSavedUntouched_IsNotAnEditOfTheDueDay_SoNoDueDayIsInventedInGoogle()
    {
        var s = await SetUpAsync();
        var item = await AddGoogleTaskAsync(s, defaultedDeadline: true);

        await SaveTaskAsync(s, item, f => f with { Title = "My title" });   // the form resends the placeholder it showed

        var stored = await ReloadAsync(s, item.Id);
        Assert.Equal(MirroredFields.Title, stored.LocalEditedFields);
        Assert.False(stored.IsDeadlineDefaulted);                            // the app accepts it...
    }

    [Fact]
    public async Task GoogleTask_SecondEditBeforeASync_AddsToTheFirst()
    {
        var s = await SetUpAsync();
        var item = await AddGoogleTaskAsync(s);
        await SaveTaskAsync(s, item, f => f with { Title = "My title" });
        var firstTime = (await ReloadAsync(s, item.Id)).LocalEditedAtUtc;
        await Task.Delay(20);

        item = await ReloadAsync(s, item.Id);
        await SaveTaskAsync(s, item, f => f with { Description = "My notes" });

        var stored = await ReloadAsync(s, item.Id);
        Assert.Equal(MirroredFields.Title | MirroredFields.Description, stored.LocalEditedFields);
        Assert.True(stored.LocalEditedAtUtc > firstTime);
    }

    [Fact]
    public async Task TheList_ShowsWhichImportedItemsHaveAnEditThatWasNotSentYet()
    {
        var s = await SetUpAsync();
        var edited = await AddGoogleTaskAsync(s);
        var untouched = await AddImportedEventAsync(s);
        await SaveTaskAsync(s, edited, f => f with { Title = "My title" });

        var list = await new GetTasksByTeamQueryHandler(s.Db, Cache).Handle(new GetTasksByTeamQuery(s.Team.Id), CancellationToken.None);

        Assert.True(list.Single(t => t.Id == edited.Id).HasUnsentChanges);
        Assert.False(list.Single(t => t.Id == untouched.Id).HasUnsentChanges);
    }

    // ---- an imported event ----

    [Fact]
    public async Task Event_EditedFields_AreNotedWithTheirKinds()
    {
        var s = await SetUpAsync();
        var item = await AddImportedEventAsync(s);

        await SaveEventAsync(s, item, f => f with
        {
            Title = "Daily", Location = "Room 7", ColorKey = "tomato", IsBusy = false, StartUtc = Start.AddHours(2), EndUtc = Start.AddHours(3),
            Description = "Agenda"
        });

        Assert.Equal(
            MirroredFields.Title | MirroredFields.Description | MirroredFields.Time | MirroredFields.Location | MirroredFields.Color | MirroredFields.Busy,
            (await ReloadAsync(s, item.Id)).LocalEditedFields);
    }

    [Fact]
    public async Task Event_AlertsChanged_AreNoted_AndTheSameAlertsSavedAgainAreNot()
    {
        var s = await SetUpAsync();
        var item = await AddImportedEventAsync(s);

        await SaveEventAsync(s, item, f => f with { Reminders = [new ReminderInput(10, null, null)] });
        Assert.Equal(MirroredFields.Reminders, (await ReloadAsync(s, item.Id)).LocalEditedFields);

        // Another item whose alerts are saved unchanged: nothing to send.
        var other = await AddImportedEventAsync(s, eventId: "e2");
        await SaveEventAsync(s, other, f => f with { Reminders = [] });
        Assert.Equal(MirroredFields.None, (await ReloadAsync(s, other.Id)).LocalEditedFields);
    }

    [Fact]
    public async Task Event_OnlyImpactAndEffortChanged_NothingIsNoted()
    {
        var s = await SetUpAsync();
        var item = await AddImportedEventAsync(s);

        await SaveEventAsync(s, item, f => f with { ImpactScore = 9, EstimatedEffortHours = 2 });

        Assert.Equal(MirroredFields.None, (await ReloadAsync(s, item.Id)).LocalEditedFields);
    }

    [Theory]
    [InlineData(EventTemplate.Birthday)]
    [InlineData(EventTemplate.WorkingLocation)]
    public async Task EventsGoogleManagesItself_AreEditableHere_ButNothingIsNoted(EventTemplate template)
    {
        var s = await SetUpAsync();
        var item = await AddImportedEventAsync(s, template);

        await SaveEventAsync(s, item, f => f with { Title = "Mine" });

        var stored = await ReloadAsync(s, item.Id);
        Assert.Equal("Mine", stored.Title);
        Assert.Equal(MirroredFields.None, stored.LocalEditedFields);
    }

    [Fact]
    public async Task ItemCreatedInTheApp_IsNeverNoted()
    {
        var s = await SetUpAsync();
        var item = await AddImportedEventAsync(s);
        item.ExternalCalendarEventId = null;
        await s.Db.SaveChangesAsync();

        await SaveEventAsync(s, item, f => f with { Title = "Mine" });

        Assert.Equal(MirroredFields.None, (await ReloadAsync(s, item.Id)).LocalEditedFields);
    }

    // ---- an imported series: every occurrence is an event of its own in Google ----

    [Fact]
    public async Task ImportedSeries_All_NotesEveryOccurrence()
    {
        var s = await SetUpAsync();
        var rows = new List<CalendarItem>();
        for (var i = 0; i < 3; i++)
        {
            rows.Add(await AddImportedEventAsync(s, seriesId: "g-series", dayOffset: 7 * i, eventId: $"g-{i}"));
        }

        await SaveEventAsync(s, rows[1], f => f with { Title = "Renamed" }, SeriesScope.All);

        var after = await s.Db.CalendarItems.AsNoTracking().ToListAsync();
        Assert.All(after, r => Assert.Equal(MirroredFields.Title, r.LocalEditedFields));
        Assert.All(after, r => Assert.NotNull(r.LocalEditedAtUtc));
    }

    [Fact]
    public async Task ImportedSeries_This_NotesOnlyTheEditedOccurrence()
    {
        var s = await SetUpAsync();
        var rows = new List<CalendarItem>();
        for (var i = 0; i < 3; i++)
        {
            rows.Add(await AddImportedEventAsync(s, seriesId: "g-series", dayOffset: 7 * i, eventId: $"g-{i}"));
        }

        await SaveEventAsync(s, rows[1], f => f with { Title = "Renamed" }, SeriesScope.This);

        var after = await s.Db.CalendarItems.AsNoTracking().ToListAsync();
        Assert.Equal([rows[1].Id], after.Where(r => r.LocalEditedFields != MirroredFields.None).Select(r => r.Id));
    }

    // ---- many at once ----

    [Fact]
    public async Task BulkDone_NotesTheGoogleTasks_AndLeavesEventsAndTasksOfTheAppAlone()
    {
        var s = await SetUpAsync();
        var googleTask = await AddGoogleTaskAsync(s);
        var ownTask = new CalendarItem { TeamId = s.Team.Id, AssignedUserId = s.Owner.Id, Title = "Own", TaskType = TaskType.Task, Status = TaskStatus.Backlog, DeadlineUtc = Day(4) };
        s.Db.CalendarItems.Add(ownTask);
        await s.Db.SaveChangesAsync();

        var result = await new BulkSetStatusCommandHandler(s.Db, Cache, new NotificationService(s.Db)).Handle(
            new BulkSetStatusCommand(s.Team.Id, [googleTask.Id, ownTask.Id], TaskStatus.Completed, s.Owner.Id, CanActOnAny: true), CancellationToken.None);

        Assert.Equal(2, result.Succeeded);
        Assert.Equal(MirroredFields.Completed, (await ReloadAsync(s, googleTask.Id)).LocalEditedFields);
        Assert.Equal(MirroredFields.None, (await ReloadAsync(s, ownTask.Id)).LocalEditedFields);
    }
}
