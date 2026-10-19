using Microsoft.EntityFrameworkCore;
using Moq;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Application.Tasks.Commands.CreateTask;
using PlanFlow.Application.Tasks.Commands.DeleteTask;
using PlanFlow.Application.Tasks.Commands.UpdateTask;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;
using PlanFlow.Infrastructure.Persistence;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Application.Tasks.Commands;

/// <summary>
/// Creating, editing and deleting an item that repeats. An event series is created 90 days ahead; the "This / This and
/// following / All" choice decides which occurrences an edit or a delete reaches.
/// </summary>
public class RepeatingItemsTests
{
    private static ICacheService Cache => new Mock<ICacheService>().Object;

    private static readonly DateTime Tomorrow9 = DateTime.UtcNow.Date.AddDays(1).AddHours(9);

    private sealed record Setup(PlanFlowDbContext Db, Team Team, User Owner, User Member);

    private static async Task<Setup> SetUpAsync()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db, "Olga");
        var member = await TestDb.AddUserAsync(db, "Marek");
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (member, TeamRole.Member));
        return new Setup(db, team, owner, member);
    }

    private static Task<TaskDto> CreateAsync(Setup s, CreateTaskCommand command) =>
        new CreateTaskCommandHandler(s.Db, Cache, new NotificationService(s.Db)).Handle(command, CancellationToken.None);

    private static CreateTaskCommand DailyEvent(Setup s, Guid? assignee = null) =>
        new(s.Team.Id, "Standup", null, assignee ?? s.Owner.Id, null, 5, null, s.Owner.Id, TaskType.Event, Tomorrow9, null, true,
            RecurrenceFrequency.Daily, 1, null, null, EndUtc: Tomorrow9.AddMinutes(30));

    private static Task<TaskDto> UpdateAsync(Setup s, UpdateTaskCommand command) =>
        new UpdateTaskCommandHandler(s.Db, Cache, new NotificationService(s.Db)).Handle(command, CancellationToken.None);

    /// <summary>What the edit form sends for an occurrence that has not been changed yet: its stored fields and its series' rule.</summary>
    private static UpdateTaskCommand Form(Setup s, CalendarItem item, SeriesScope scope)
    {
        var series = item.RecurrenceSeriesId is { } id ? s.Db.RecurrenceSeries.AsNoTracking().Single(x => x.Id == id) : null;
        return new UpdateTaskCommand(
            item.Id, item.Title, item.Description, item.Status, item.AssignedUserId, item.DeadlineUtc, item.ImpactScore, item.BlockedByTaskId,
            null, null, s.Owner.Id, item.TaskType, item.StartUtc, item.EstimatedEffortHours, series is not null, series?.Frequency, series?.Interval,
            series is null || series.Weekdays == 0 ? null : series.Weekdays, series?.EndsAtUtc, EndUtc: item.EndUtc, EventTemplate: item.EventTemplate,
            IsAllDay: item.IsAllDay, Location: item.Location, ColorKey: item.ColorKey, IsBusy: item.IsBusy, Scope: scope);
    }

    private static List<CalendarItem> Occurrences(Setup s, Guid seriesId) =>
        s.Db.CalendarItems.AsNoTracking().Where(i => i.RecurrenceSeriesId == seriesId).AsEnumerable().OrderBy(i => i.DueUtc).ToList();

    private static async Task<(Guid SeriesId, List<CalendarItem> Rows)> SeedDailyEventAsync(Setup s, Guid? assignee = null)
    {
        var dto = await CreateAsync(s, DailyEvent(s, assignee));
        var seriesId = Guid.Parse(dto.SeriesId!);
        return (seriesId, Occurrences(s, seriesId));
    }

    private static Task DeleteAsync(Setup s, Guid id, SeriesScope scope, User? actor = null, bool any = true) =>
        new DeleteTaskCommandHandler(s.Db, Cache, new NotificationService(s.Db))
            .Handle(new DeleteTaskCommand(id, (actor ?? s.Owner).Id, scope, any), CancellationToken.None);

    // ---- creating ----

    [Fact]
    public async Task Create_RepeatingEvent_MakesTheSeriesAndTheOccurrencesForTheNext90Days()
    {
        var s = await SetUpAsync();

        var dto = await CreateAsync(s, DailyEvent(s));

        Assert.True(dto.IsRecurring);
        Assert.Equal(RecurrenceFrequency.Daily, dto.RecurrenceFrequency);
        Assert.Equal(1, dto.RecurrenceInterval);
        Assert.NotNull(dto.SeriesId);
        var rows = Occurrences(s, Guid.Parse(dto.SeriesId!));
        Assert.Equal(Tomorrow9, rows[0].StartUtc);
        Assert.InRange(rows.Count, 88, 92);
        Assert.True(rows.Last().StartUtc <= DateTime.UtcNow.AddDays(90));
        Assert.All(rows, r => Assert.Equal(TimeSpan.FromMinutes(30), r.EndUtc - r.StartUtc));
        Assert.All(rows, r => Assert.True(r.CurrentUrgencyScore >= 0));
        var series = s.Db.RecurrenceSeries.Single();
        Assert.True(series.GeneratedThroughUtc >= DateTime.UtcNow.AddDays(89));
    }

    [Fact]
    public async Task Create_RepeatingEvent_GivesEveryOccurrenceTheAlertsAndTheGuests()
    {
        var s = await SetUpAsync();
        var command = DailyEvent(s) with
        {
            Reminders = [new ReminderInput(15, null, null)],
            ParticipantUserIds = [s.Member.Id]
        };

        var dto = await CreateAsync(s, command);

        var ids = Occurrences(s, Guid.Parse(dto.SeriesId!)).Select(r => r.Id).ToList();
        Assert.All(ids, id => Assert.Single(s.Db.ItemReminders, r => r.CalendarItemId == id && r.OffsetMinutes == 15));
        Assert.All(ids, id => Assert.Single(s.Db.ItemParticipants, p => p.CalendarItemId == id && p.UserId == s.Member.Id));
    }

    [Fact]
    public async Task Create_RepeatingTask_IsOneOpenOccurrence_TheNextComesWhenItIsCompleted()
    {
        var s = await SetUpAsync();
        var deadline = DateTime.UtcNow.AddDays(3);

        var dto = await CreateAsync(s, new CreateTaskCommand(
            s.Team.Id, "Report", null, s.Owner.Id, deadline, 5, null, s.Owner.Id, IsRecurring: true,
            RecurrenceFrequency: RecurrenceFrequency.Weekly, RecurrenceInterval: 1, RecurrenceWeekdays: null));

        Assert.Single(s.Db.CalendarItems);
        var series = s.Db.RecurrenceSeries.Single();
        Assert.Equal(TaskType.Task, series.TaskType);
        Assert.Equal(deadline, series.GeneratedThroughUtc);
        Assert.Equal(RecurrenceFrequency.Weekly, dto.RecurrenceFrequency);
        Assert.NotEqual(0, dto.RecurrenceWeekdays); // a weekly rule stores its day: the weekday of the first occurrence
    }

    [Fact]
    public async Task Create_NotRepeating_HasNoSeries()
    {
        var s = await SetUpAsync();

        var dto = await CreateAsync(s, DailyEvent(s) with { IsRecurring = false, RecurrenceFrequency = null, RecurrenceInterval = null });

        Assert.False(dto.IsRecurring);
        Assert.Null(dto.SeriesId);
        Assert.Empty(s.Db.RecurrenceSeries);
        Assert.Single(s.Db.CalendarItems);
    }

    // ---- editing: This ----

    [Fact]
    public async Task Edit_This_ChangesOnlyThatOccurrence_AndMarksItAsAnException()
    {
        var s = await SetUpAsync();
        var (seriesId, rows) = await SeedDailyEventAsync(s);
        var target = rows[5];

        await UpdateAsync(s, Form(s, target, SeriesScope.This) with { Title = "Standup (garden)", Location = "Garden" });

        var after = Occurrences(s, seriesId);
        Assert.Equal(rows.Count, after.Count);
        var changed = Assert.Single(after, r => r.Title == "Standup (garden)");
        Assert.Equal(target.Id, changed.Id);
        Assert.True(changed.IsSeriesException);
        Assert.All(after.Where(r => r.Id != target.Id), r => { Assert.Equal("Standup", r.Title); Assert.False(r.IsSeriesException); });
    }

    [Fact]
    public async Task Edit_This_WithoutChangingAnything_DoesNotMakeAnException()
    {
        var s = await SetUpAsync();
        var (seriesId, rows) = await SeedDailyEventAsync(s);

        await UpdateAsync(s, Form(s, rows[2], SeriesScope.This) with { Status = TaskStatus.Tentative });

        Assert.All(Occurrences(s, seriesId), r => Assert.False(r.IsSeriesException));
    }

    [Fact]
    public async Task Edit_This_IgnoresTheRepeatRule_TheRuleBelongsToTheSeries()
    {
        var s = await SetUpAsync();
        var (seriesId, rows) = await SeedDailyEventAsync(s);

        var dto = await UpdateAsync(s, Form(s, rows[3], SeriesScope.This) with
        {
            IsRecurring = false, RecurrenceFrequency = RecurrenceFrequency.Monthly, RecurrenceInterval = 3
        });

        Assert.True(dto.IsRecurring);
        Assert.Equal(RecurrenceFrequency.Daily, dto.RecurrenceFrequency);
        Assert.Equal(rows.Count, Occurrences(s, seriesId).Count);
    }

    // ---- editing: All ----

    [Fact]
    public async Task Edit_All_PassesTheContentAndTheTimeOfDayOn_ButEveryOccurrenceKeepsItsOwnDay()
    {
        var s = await SetUpAsync();
        var (seriesId, rows) = await SeedDailyEventAsync(s);
        var target = rows[2];
        var originalDays = rows.ToDictionary(r => r.Id, r => r.StartUtc!.Value.Date);

        await UpdateAsync(s, Form(s, target, SeriesScope.All) with
        {
            Title = "Planning", Location = "Room 7", StartUtc = target.StartUtc!.Value.Date.AddHours(10).AddMinutes(30),
            EndUtc = target.StartUtc!.Value.Date.AddHours(11).AddMinutes(30)
        });

        var after = Occurrences(s, seriesId);
        Assert.Equal(rows.Count, after.Count);
        Assert.All(after, r =>
        {
            Assert.Equal("Planning", r.Title);
            Assert.Equal("Room 7", r.Location);
            Assert.Equal(new TimeSpan(10, 30, 0), r.StartUtc!.Value.TimeOfDay);
            Assert.Equal(TimeSpan.FromHours(1), r.EndUtc - r.StartUtc);
            Assert.Equal(originalDays[r.Id], r.StartUtc.Value.Date);
            Assert.False(r.IsSeriesException);
        });
    }

    [Fact]
    public async Task Edit_All_MovingTheDayOfOneOccurrence_MovesOnlyThatOne()
    {
        var s = await SetUpAsync();
        var (seriesId, rows) = await SeedDailyEventAsync(s);
        var target = rows[4];

        await UpdateAsync(s, Form(s, target, SeriesScope.All) with
        {
            StartUtc = target.StartUtc!.Value.AddDays(1), EndUtc = target.EndUtc!.Value.AddDays(1)
        });

        var after = Occurrences(s, seriesId).ToDictionary(r => r.Id);
        Assert.Equal(target.StartUtc.Value.AddDays(1), after[target.Id].StartUtc);
        Assert.All(rows.Where(r => r.Id != target.Id), r => Assert.Equal(r.StartUtc, after[r.Id].StartUtc));
    }

    [Fact]
    public async Task Edit_All_ChangingTheRule_MakesTheFutureOccurrencesAgainFromTheNewRule()
    {
        var s = await SetUpAsync();
        var (seriesId, rows) = await SeedDailyEventAsync(s);
        var target = rows[3];
        var monday = RecurrenceRules.Weekdays(DayOfWeek.Monday);

        var dto = await UpdateAsync(s, Form(s, target, SeriesScope.All) with
        {
            RecurrenceFrequency = RecurrenceFrequency.Weekly, RecurrenceInterval = 1, RecurrenceWeekdays = monday
        });

        Assert.Equal(RecurrenceFrequency.Weekly, dto.RecurrenceFrequency);
        Assert.Equal(monday, dto.RecurrenceWeekdays);
        var after = Occurrences(s, seriesId);
        Assert.InRange(after.Count, 12, 16); // about thirteen Mondays in 90 days, plus the edited occurrence
        Assert.All(after.Where(r => r.Id != target.Id), r => Assert.Equal(DayOfWeek.Monday, r.StartUtc!.Value.DayOfWeek));
        Assert.Contains(after, r => r.Id == target.Id); // the occurrence that was edited is kept
        Assert.Equal(RecurrenceFrequency.Weekly, s.Db.RecurrenceSeries.Single().Frequency);
    }

    [Fact]
    public async Task Edit_All_SwitchingRepeatsOff_KeepsOnlyTheEditedOccurrence_AsAnOrdinaryItem()
    {
        var s = await SetUpAsync();
        var (seriesId, rows) = await SeedDailyEventAsync(s);

        var dto = await UpdateAsync(s, Form(s, rows[1], SeriesScope.All) with { IsRecurring = false });

        Assert.False(dto.IsRecurring);
        Assert.Null(dto.SeriesId);
        Assert.Empty(s.Db.RecurrenceSeries);
        var left = Assert.Single(s.Db.CalendarItems);
        Assert.Equal(rows[1].Id, left.Id);
        Assert.Null(left.RecurrenceSeriesId);
    }

    [Fact]
    public async Task Edit_All_CanHandTheWholeSeriesToSomeoneElse()
    {
        var s = await SetUpAsync();
        var (seriesId, rows) = await SeedDailyEventAsync(s);

        await UpdateAsync(s, Form(s, rows[0], SeriesScope.All) with { AssignedUserId = s.Member.Id });

        var after = Occurrences(s, seriesId);
        Assert.All(after, r => Assert.Equal(s.Member.Id, r.AssignedUserId));
        Assert.All(after, r => Assert.True(r.IsAssignmentLocked)); // an Owner handed it over
    }

    [Fact]
    public async Task Edit_All_LeavesTheAssigneeAloneWhenTheEditDidNotChangeIt()
    {
        var s = await SetUpAsync();
        var (seriesId, rows) = await SeedDailyEventAsync(s);
        var odd = await s.Db.CalendarItems.FirstAsync(i => i.Id == rows[7].Id);
        odd.AssignedUserId = s.Member.Id; // one occurrence was given to someone else on its own
        await s.Db.SaveChangesAsync();

        await UpdateAsync(s, Form(s, rows[0], SeriesScope.All) with { Title = "Daily" });

        var after = Occurrences(s, seriesId);
        Assert.Equal(s.Member.Id, after.Single(r => r.Id == odd.Id).AssignedUserId);
        Assert.All(after, r => Assert.Equal("Daily", r.Title));
    }

    // ---- who may pass a change on ----

    [Fact]
    public async Task Edit_All_ByAMember_IsRefusedWhileAnOccurrenceIsAssignedToSomeoneElse()
    {
        var s = await SetUpAsync();
        var (_, rows) = await SeedDailyEventAsync(s, assignee: s.Member.Id);
        var theirs = await s.Db.CalendarItems.FirstAsync(i => i.Id == rows[6].Id);
        theirs.AssignedUserId = s.Owner.Id;
        await s.Db.SaveChangesAsync();

        var attempt = () => UpdateAsync(s, Form(s, rows[0], SeriesScope.All) with { Title = "Mine now", UpdatedByUserId = s.Member.Id, CallerIsManager = false });

        var error = await Assert.ThrowsAsync<ForbiddenAccessException>(attempt);
        Assert.Equal(SeriesEditing.NotYoursMessage, error.Message);
    }

    [Fact]
    public async Task Edit_All_ByAMember_WorksWhenEveryOccurrenceIsAssignedToThem()
    {
        var s = await SetUpAsync();
        var (seriesId, rows) = await SeedDailyEventAsync(s, assignee: s.Member.Id);

        await UpdateAsync(s, Form(s, rows[0], SeriesScope.All) with { Title = "Team standup", UpdatedByUserId = s.Member.Id, CallerIsManager = false });

        Assert.All(Occurrences(s, seriesId), r => Assert.Equal("Team standup", r.Title));
    }

    [Fact]
    public async Task Edit_This_ByAMember_NeverTouchesTheOtherOccurrences()
    {
        var s = await SetUpAsync();
        var (seriesId, rows) = await SeedDailyEventAsync(s, assignee: s.Member.Id);
        var theirs = await s.Db.CalendarItems.FirstAsync(i => i.Id == rows[6].Id);
        theirs.AssignedUserId = s.Owner.Id;
        await s.Db.SaveChangesAsync();

        await UpdateAsync(s, Form(s, rows[0], SeriesScope.This) with { Title = "Once", UpdatedByUserId = s.Member.Id, CallerIsManager = false });

        Assert.Single(Occurrences(s, seriesId), r => r.Title == "Once");
    }

    // ---- editing: This and following ----

    [Fact]
    public async Task Edit_ThisAndFollowing_CutsTheSeriesInTwo_TheEarlierPartKeepsItsOldRule()
    {
        var s = await SetUpAsync();
        var (seriesId, rows) = await SeedDailyEventAsync(s);
        var target = rows[10];

        var dto = await UpdateAsync(s, Form(s, target, SeriesScope.ThisAndFollowing) with { Title = "Standup (new format)" });

        var tailId = Guid.Parse(dto.SeriesId!);
        Assert.NotEqual(seriesId, tailId);
        var head = Occurrences(s, seriesId);
        var tail = Occurrences(s, tailId);
        Assert.Equal(10, head.Count);
        Assert.Equal(rows.Count - 10, tail.Count);
        Assert.All(head, r => Assert.Equal("Standup", r.Title));
        Assert.All(tail, r => Assert.Equal("Standup (new format)", r.Title));
        var headSeries = s.Db.RecurrenceSeries.Single(x => x.Id == seriesId);
        Assert.Equal(rows[9].StartUtc, headSeries.EndsAtUtc); // the earlier part ends where it was cut
        var tailSeries = s.Db.RecurrenceSeries.Single(x => x.Id == tailId);
        Assert.Equal(RecurrenceFrequency.Daily, tailSeries.Frequency);
        Assert.Equal(target.StartUtc, tailSeries.AnchorUtc);
    }

    [Fact]
    public async Task Edit_ThisAndFollowing_ThenAllOnTheLaterPart_NeverTouchesTheEarlierPart()
    {
        var s = await SetUpAsync();
        var (seriesId, rows) = await SeedDailyEventAsync(s);
        var dto = await UpdateAsync(s, Form(s, rows[10], SeriesScope.ThisAndFollowing) with { Title = "B" });
        var tailRows = Occurrences(s, Guid.Parse(dto.SeriesId!));

        await UpdateAsync(s, Form(s, tailRows[3], SeriesScope.All) with { Title = "C" });

        Assert.All(Occurrences(s, seriesId), r => Assert.Equal("Standup", r.Title));
        Assert.All(Occurrences(s, Guid.Parse(dto.SeriesId!)), r => Assert.Equal("C", r.Title));
    }

    [Fact]
    public async Task Edit_ThisAndFollowing_WithANewRule_MakesTheLaterOccurrencesAgain()
    {
        var s = await SetUpAsync();
        var (seriesId, rows) = await SeedDailyEventAsync(s);
        var target = rows[10];

        var dto = await UpdateAsync(s, Form(s, target, SeriesScope.ThisAndFollowing) with { RecurrenceInterval = 2 });

        var tail = Occurrences(s, Guid.Parse(dto.SeriesId!));
        Assert.Equal(10, Occurrences(s, seriesId).Count); // untouched
        Assert.InRange(tail.Count, 40, 50); // every second day over the remaining ~80 days
        Assert.Equal(target.Id, tail[0].Id);
        for (var i = 1; i < tail.Count; i++)
        {
            Assert.Equal(TimeSpan.FromDays(2), tail[i].StartUtc - tail[i - 1].StartUtc);
        }
    }

    [Fact]
    public async Task Edit_ThisAndFollowing_OnTheFirstOccurrence_IsTheSameAsAll()
    {
        var s = await SetUpAsync();
        var (seriesId, rows) = await SeedDailyEventAsync(s);

        var dto = await UpdateAsync(s, Form(s, rows[0], SeriesScope.ThisAndFollowing) with { Title = "Daily" });

        Assert.Equal(seriesId.ToString(), dto.SeriesId);
        Assert.Single(s.Db.RecurrenceSeries);
        Assert.All(Occurrences(s, seriesId), r => Assert.Equal("Daily", r.Title));
    }

    [Fact]
    public async Task Edit_ThisAndFollowing_SwitchingRepeatsOff_EndsTheSeriesBeforeIt()
    {
        var s = await SetUpAsync();
        var (seriesId, rows) = await SeedDailyEventAsync(s);

        var dto = await UpdateAsync(s, Form(s, rows[10], SeriesScope.ThisAndFollowing) with { IsRecurring = false });

        Assert.False(dto.IsRecurring);
        Assert.Equal(10, Occurrences(s, seriesId).Count);
        Assert.Equal(11, s.Db.CalendarItems.Count()); // the ten earlier ones and the edited one, now ordinary
        Assert.Equal(rows[9].StartUtc, s.Db.RecurrenceSeries.Single().EndsAtUtc);
    }

    // ---- editing: starting a series ----

    [Fact]
    public async Task Edit_SwitchingRepeatsOn_MakesTheItemTheFirstOccurrenceOfANewSeries()
    {
        var s = await SetUpAsync();
        var single = await CreateAsync(s, DailyEvent(s) with { IsRecurring = false, RecurrenceFrequency = null, RecurrenceInterval = null });
        var item = await s.Db.CalendarItems.AsNoTracking().SingleAsync();

        var dto = await UpdateAsync(s, Form(s, item, SeriesScope.This) with
        {
            IsRecurring = true, RecurrenceFrequency = RecurrenceFrequency.Weekly, RecurrenceInterval = 1
        });

        Assert.True(dto.IsRecurring);
        var rows = Occurrences(s, Guid.Parse(dto.SeriesId!));
        Assert.InRange(rows.Count, 12, 14);
        Assert.Equal(item.Id, rows[0].Id);
        Assert.All(rows, r => Assert.Equal(item.StartUtc!.Value.DayOfWeek, r.StartUtc!.Value.DayOfWeek));
    }

    // ---- an imported (Google) series ----

    private static async Task<List<CalendarItem>> SeedImportedSeriesAsync(Setup s)
    {
        var rows = Enumerable.Range(0, 4).Select(i => new CalendarItem
        {
            TeamId = s.Team.Id, AssignedUserId = s.Owner.Id, Title = "Google weekly", TaskType = TaskType.Event, Status = TaskStatus.Confirmed,
            StartUtc = Tomorrow9.AddDays(7 * i), EndUtc = Tomorrow9.AddDays(7 * i).AddHours(1), SeriesId = "g-series-1",
            ExternalCalendarEventId = $"g-{i}"
        }).ToList();
        s.Db.CalendarItems.AddRange(rows);
        await s.Db.SaveChangesAsync();
        return rows;
    }

    private static UpdateTaskCommand ImportedForm(Setup s, CalendarItem item, SeriesScope scope) =>
        Form(s, item, scope) with { ImpactScore = 9, EstimatedEffortHours = 2.0, Title = "Not Google's to rename" };

    [Fact]
    public async Task ImportedSeries_All_PassesOnOnlyWhatPlanFlowAdds()
    {
        var s = await SetUpAsync();
        var rows = await SeedImportedSeriesAsync(s);

        await UpdateAsync(s, ImportedForm(s, rows[1], SeriesScope.All));

        var after = s.Db.CalendarItems.AsNoTracking().ToList();
        Assert.All(after, r => { Assert.Equal(9, r.ImpactScore); Assert.Equal(2.0, r.EstimatedEffortHours); Assert.Equal("Google weekly", r.Title); });
    }

    [Fact]
    public async Task ImportedSeries_ThisAndFollowing_ReachesTheLaterOccurrencesOnly()
    {
        var s = await SetUpAsync();
        var rows = await SeedImportedSeriesAsync(s);

        await UpdateAsync(s, ImportedForm(s, rows[2], SeriesScope.ThisAndFollowing));

        var after = s.Db.CalendarItems.AsNoTracking().AsEnumerable().OrderBy(r => r.StartUtc).ToList();
        Assert.Equal([null, null, 9, 9], after.Select(r => r.ImpactScore).ToArray());
    }

    [Fact]
    public async Task ImportedSeries_This_ChangesOnlyThatOccurrence()
    {
        var s = await SetUpAsync();
        var rows = await SeedImportedSeriesAsync(s);

        await UpdateAsync(s, ImportedForm(s, rows[1], SeriesScope.This));

        Assert.Single(s.Db.CalendarItems, r => r.ImpactScore == 9);
    }

    // ---- deleting ----

    [Fact]
    public async Task Delete_This_RemovesOneOccurrence_TheRestStays_AndItIsNotMadeAgain()
    {
        var s = await SetUpAsync();
        var (seriesId, rows) = await SeedDailyEventAsync(s);

        await DeleteAsync(s, rows[4].Id, SeriesScope.This);

        var after = Occurrences(s, seriesId);
        Assert.Equal(rows.Count - 1, after.Count);
        Assert.DoesNotContain(after, r => r.Id == rows[4].Id);
        // The recurring job only looks past the cursor, so the gap stays a gap.
        Assert.True(s.Db.RecurrenceSeries.Single().GeneratedThroughUtc > DateTime.UtcNow.AddDays(80));
    }

    [Fact]
    public async Task Delete_ThisAndFollowing_RemovesItAndTheLaterOnes_TheSeriesEndsBeforeIt()
    {
        var s = await SetUpAsync();
        var (seriesId, rows) = await SeedDailyEventAsync(s);

        await DeleteAsync(s, rows[10].Id, SeriesScope.ThisAndFollowing);

        Assert.Equal(10, Occurrences(s, seriesId).Count);
        Assert.Equal(rows[9].StartUtc, s.Db.RecurrenceSeries.Single().EndsAtUtc);
    }

    [Fact]
    public async Task Delete_All_RemovesEveryOccurrenceAndTheSeries()
    {
        var s = await SetUpAsync();
        var (_, rows) = await SeedDailyEventAsync(s);

        await DeleteAsync(s, rows[3].Id, SeriesScope.All);

        Assert.Empty(s.Db.CalendarItems);
        Assert.Empty(s.Db.RecurrenceSeries);
    }

    [Fact]
    public async Task Delete_All_ByAMember_RemovesOnlyTheOccurrencesAssignedToThem()
    {
        var s = await SetUpAsync();
        var (seriesId, rows) = await SeedDailyEventAsync(s, assignee: s.Member.Id);
        var someoneElses = await s.Db.CalendarItems.FirstAsync(i => i.Id == rows[6].Id);
        someoneElses.AssignedUserId = s.Owner.Id;
        await s.Db.SaveChangesAsync();

        await DeleteAsync(s, rows[0].Id, SeriesScope.All, actor: s.Member, any: false);

        var left = Assert.Single(s.Db.CalendarItems);
        Assert.Equal(rows[6].Id, left.Id);
    }

    [Fact]
    public async Task Delete_ThisOfTheOpenTaskOccurrence_BringsTheNextOne()
    {
        var s = await SetUpAsync();
        var deadline = DateTime.UtcNow.AddDays(3);
        var dto = await CreateAsync(s, new CreateTaskCommand(
            s.Team.Id, "Report", null, s.Owner.Id, deadline, 5, null, s.Owner.Id, IsRecurring: true,
            RecurrenceFrequency: RecurrenceFrequency.Weekly, RecurrenceInterval: 1));

        await DeleteAsync(s, dto.Id, SeriesScope.This);

        var next = Assert.Single(s.Db.CalendarItems);
        Assert.NotEqual(dto.Id, next.Id);
        Assert.Equal(deadline.AddDays(7), next.DeadlineUtc);
        Assert.Equal(next.DeadlineUtc, s.Db.RecurrenceSeries.Single().GeneratedThroughUtc);
    }

    [Fact]
    public async Task Delete_AnItemThatDoesNotRepeat_IgnoresTheScope()
    {
        var s = await SetUpAsync();
        var dto = await CreateAsync(s, DailyEvent(s) with { IsRecurring = false, RecurrenceFrequency = null, RecurrenceInterval = null });
        await CreateAsync(s, DailyEvent(s) with { IsRecurring = false, RecurrenceFrequency = null, RecurrenceInterval = null });

        await DeleteAsync(s, dto.Id, SeriesScope.All);

        Assert.Single(s.Db.CalendarItems); // the other plain event is not a "series" and stays
    }
}
