using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Moq;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Application.Tasks.Commands.CreateTask;
using PlanFlow.Application.Tasks.Commands.UpdateTask;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Application.Tasks.Queries.GetTaskDetail;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Application.Tasks.Commands;

/// <summary>
/// The alerts of an item. The shape of one alert is checked by the validator, what an item can carry (all-day, deadline,
/// template) by the handler; saving keeps the rows that stay, so an alert that already went off does not go off again.
/// </summary>
public class ItemAlertsTests
{
    private static ICacheService Cache => new Mock<ICacheService>().Object;

    private static readonly DateTime NextWeek = DateTime.UtcNow.Date.AddDays(7);

    private static ReminderInput Before(int minutes, ReminderAnchor anchor = ReminderAnchor.Start) => new(minutes, null, null, anchor);

    private static ReminderInput DayBefore(int days, int minutesOfDay, ReminderAnchor anchor = ReminderAnchor.Start) => new(null, days, minutesOfDay, anchor);

    private static async Task<(PlanFlowDbContext Db, Guid TeamId, Guid UserId)> SetUpAsync()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner));
        return (db, team.Id, owner.Id);
    }

    private static CreateTaskCommand NewEvent(
        Guid teamId, Guid userId, IReadOnlyList<ReminderInput>? reminders, bool allDay = false, EventTemplate template = EventTemplate.Default) =>
        new(teamId, "Standup", null, null, null, 5, null, userId, TaskType.Event,
            StartUtc: NextWeek.AddHours(9), EndUtc: allDay ? null : NextWeek.AddHours(10), IsAllDay: allDay, EventTemplate: template, Reminders: reminders);

    private static CreateTaskCommand NewTask(
        Guid teamId, Guid userId, IReadOnlyList<ReminderInput>? reminders, DateTime? deadline, DateTime? start = null) =>
        new(teamId, "Report", null, null, deadline, 5, null, userId, StartUtc: start, Reminders: reminders);

    private static Task<TaskDto> CreateAsync(PlanFlowDbContext db, CreateTaskCommand command) =>
        new CreateTaskCommandHandler(db, Cache, new NotificationService(db)).Handle(command, CancellationToken.None);

    private static async Task<List<ItemReminder>> RemindersOf(PlanFlowDbContext db, Guid itemId) =>
        await db.ItemReminders.Where(r => r.CalendarItemId == itemId).ToListAsync();

    private static Task<TaskDto> UpdateAsync(PlanFlowDbContext db, CalendarItem item, IReadOnlyList<ReminderInput>? reminders, Guid actor, bool? allDay = null) =>
        new UpdateTaskCommandHandler(db, Cache, new NotificationService(db)).Handle(
            new UpdateTaskCommand(
                item.Id, item.Title, item.Description, item.Status, item.AssignedUserId, item.DeadlineUtc, item.ImpactScore, item.BlockedByTaskId,
                null, null, actor, item.TaskType, item.StartUtc, item.EstimatedEffortHours, EndUtc: item.EndUtc,
                EventTemplate: item.EventTemplate, IsAllDay: allDay ?? item.IsAllDay, Reminders: reminders),
            CancellationToken.None);

    // ---- the shape of one alert (validator) ----

    [Theory]
    [InlineData(-1, null, null)]                 // before the start means after it
    [InlineData(40321, null, null)]              // more than four weeks
    [InlineData(null, 29, 540)]                  // more than 28 days
    [InlineData(null, -1, 540)]
    [InlineData(null, 1, 1440)]                  // 24:00 is not a time of day
    [InlineData(null, 1, -1)]
    [InlineData(null, null, null)]               // neither form
    [InlineData(30, 1, 540)]                     // both forms
    [InlineData(null, 1, null)]                  // a day needs its time
    [InlineData(null, null, 540)]
    public void Validator_RefusesAMalformedAlert(int? offset, int? days, int? timeOfDay)
    {
        var command = NewTask(Guid.NewGuid(), Guid.NewGuid(), [new ReminderInput(offset, days, timeOfDay)], NextWeek, NextWeek);

        var result = new CreateTaskCommandValidator().Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.StartsWith("Reminders"));
    }

    [Fact]
    public void Validator_RefusesMoreThanFourAlerts_AndAcceptsFour()
    {
        var four = new[] { Before(5), Before(10), Before(15), Before(20) };
        var five = four.Append(Before(25)).ToArray();

        Assert.True(new CreateTaskCommandValidator().Validate(NewTask(Guid.NewGuid(), Guid.NewGuid(), four, NextWeek, NextWeek)).IsValid);
        Assert.False(new CreateTaskCommandValidator().Validate(NewTask(Guid.NewGuid(), Guid.NewGuid(), five, NextWeek, NextWeek)).IsValid);
    }

    [Fact]
    public void Validator_AcceptsBothFormsAtTheirLimits()
    {
        var limits = new[] { Before(0), Before(40320), DayBefore(0, 0), DayBefore(28, 1439) };

        Assert.True(new CreateTaskCommandValidator().Validate(NewTask(Guid.NewGuid(), Guid.NewGuid(), limits, NextWeek, NextWeek)).IsValid);
    }

    // ---- create: what the item can carry ----

    [Fact]
    public async Task Create_StoresTheAlerts_AndTheDetailReturnsThemWithTheirAnchor()
    {
        var (db, teamId, userId) = await SetUpAsync();

        var dto = await CreateAsync(db, NewTask(teamId, userId, [Before(60, ReminderAnchor.Deadline), DayBefore(1, 9 * 60)], NextWeek.AddHours(17), NextWeek));

        var detail = await new GetTaskDetailQueryHandler(db, Cache).Handle(new PlanFlow.Application.Tasks.Queries.GetTaskDetail.GetTaskDetailQuery(dto.Id), CancellationToken.None);
        Assert.Equal(2, detail.Reminders!.Count);
        Assert.Contains(detail.Reminders, r => r is { OffsetMinutes: 60, Anchor: ReminderAnchor.Deadline });
        Assert.Contains(detail.Reminders, r => r is { DaysBefore: 1, TimeOfDayMinutes: 540, Anchor: ReminderAnchor.Start });
    }

    [Fact]
    public async Task Create_WithoutAList_HasNoAlerts()
    {
        var (db, teamId, userId) = await SetUpAsync();

        var dto = await CreateAsync(db, NewEvent(teamId, userId, null));

        Assert.Empty(await RemindersOf(db, dto.Id));
    }

    [Fact]
    public async Task Create_MoreThanFourAlerts_IsRefused()
    {
        var (db, teamId, userId) = await SetUpAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateAsync(db, NewEvent(teamId, userId, [Before(1), Before(2), Before(3), Before(4), Before(5)])));
    }

    [Fact]
    public async Task Create_TwoIdenticalAlerts_AreRefused()
    {
        var (db, teamId, userId) = await SetUpAsync();

        var error = await Assert.ThrowsAsync<ValidationException>(() => CreateAsync(db, NewEvent(teamId, userId, [Before(30), Before(30)])));

        Assert.Contains("same", error.Message);
    }

    [Fact]
    public async Task Create_TheSameLeadFromTwoAnchors_IsNotADuplicate()
    {
        var (db, teamId, userId) = await SetUpAsync();

        var dto = await CreateAsync(db, NewTask(teamId, userId, [Before(60), Before(60, ReminderAnchor.Deadline)], NextWeek.AddHours(17), NextWeek.AddHours(8)));

        Assert.Equal(2, (await RemindersOf(db, dto.Id)).Count);
    }

    [Fact]
    public async Task Create_AnEventCannotBeRemindedBeforeADeadline()
    {
        var (db, teamId, userId) = await SetUpAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateAsync(db, NewEvent(teamId, userId, [Before(30, ReminderAnchor.Deadline)])));
    }

    [Fact]
    public async Task Create_AnAlertNeedsTheMomentItCountsBackFrom()
    {
        var (db, teamId, userId) = await SetUpAsync();

        // A task with no planned start cannot be reminded "before the start"; one with no deadline, not "before the deadline".
        await Assert.ThrowsAsync<ValidationException>(() => CreateAsync(db, NewTask(teamId, userId, [Before(30)], NextWeek)));
        await Assert.ThrowsAsync<ValidationException>(() => CreateAsync(db, NewTask(teamId, userId, [Before(30, ReminderAnchor.Deadline)], null, NextWeek)));
    }

    [Fact]
    public async Task Create_AnAllDayEvent_CanOnlyBeRemindedInDays()
    {
        var (db, teamId, userId) = await SetUpAsync();

        var error = await Assert.ThrowsAsync<ValidationException>(() => CreateAsync(db, NewEvent(teamId, userId, [Before(30)], allDay: true)));
        Assert.Contains("all-day", error.Message);

        var dto = await CreateAsync(db, NewEvent(teamId, userId, [DayBefore(1, 9 * 60)], allDay: true));
        Assert.Single(await RemindersOf(db, dto.Id));
    }

    [Theory]
    [InlineData(EventTemplate.OutOfOffice)]
    [InlineData(EventTemplate.WorkingLocation)]
    public async Task Create_AKindWithoutAlerts_KeepsNone_NotAnError(EventTemplate template)
    {
        var (db, teamId, userId) = await SetUpAsync();

        var dto = await CreateAsync(db, NewEvent(teamId, userId, [Before(30)], template: template));

        Assert.Empty(await RemindersOf(db, dto.Id));
    }

    [Theory]
    [InlineData(EventTemplate.FocusTime)]
    [InlineData(EventTemplate.Default)]
    public async Task Create_FocusTimeAndPlainEvents_KeepTheirAlerts(EventTemplate template)
    {
        var (db, teamId, userId) = await SetUpAsync();

        var dto = await CreateAsync(db, NewEvent(teamId, userId, [Before(30)], template: template));

        Assert.Single(await RemindersOf(db, dto.Id));
    }

    // ---- update ----

    [Fact]
    public async Task Update_NoList_LeavesTheStoredAlertsAlone()
    {
        var (db, teamId, userId) = await SetUpAsync();
        var created = await CreateAsync(db, NewEvent(teamId, userId, [Before(30), Before(60)]));
        var item = await db.CalendarItems.SingleAsync(i => i.Id == created.Id);

        await UpdateAsync(db, item, null, userId); // what "mark as done" and an older app send

        Assert.Equal(2, (await RemindersOf(db, created.Id)).Count);
    }

    [Fact]
    public async Task Update_EmptyList_ClearsThem()
    {
        var (db, teamId, userId) = await SetUpAsync();
        var created = await CreateAsync(db, NewEvent(teamId, userId, [Before(30)]));
        var item = await db.CalendarItems.SingleAsync(i => i.Id == created.Id);

        await UpdateAsync(db, item, [], userId);

        Assert.Empty(await RemindersOf(db, created.Id));
    }

    [Fact]
    public async Task Update_AlertThatStays_KeepsItsRow_AndItsMemoryOfHavingGoneOff()
    {
        var (db, teamId, userId) = await SetUpAsync();
        var created = await CreateAsync(db, NewEvent(teamId, userId, [Before(30), Before(60)]));
        var item = await db.CalendarItems.SingleAsync(i => i.Id == created.Id);
        var thirty = await db.ItemReminders.SingleAsync(r => r.CalendarItemId == created.Id && r.OffsetMinutes == 30);
        var firedFor = NextWeek.AddHours(8).AddMinutes(30);
        thirty.LastFiredForUtc = firedFor;
        await db.SaveChangesAsync();

        // 30 stays, 60 becomes 15.
        await UpdateAsync(db, item, [Before(30), Before(15)], userId);

        var stored = await RemindersOf(db, created.Id);
        Assert.Equal([15, 30], stored.Select(r => r.OffsetMinutes!.Value).OrderBy(m => m));
        var kept = stored.Single(r => r.OffsetMinutes == 30);
        Assert.Equal(thirty.Id, kept.Id);
        Assert.Equal(firedFor, kept.LastFiredForUtc);
    }

    [Fact]
    public async Task Update_AnImportedEvent_CanHaveItsOwnAlerts()
    {
        var (db, teamId, userId) = await SetUpAsync();
        var item = new CalendarItem
        {
            TeamId = teamId, AssignedUserId = userId, Title = "Offsite", TaskType = TaskType.Event, Status = TaskStatus.Confirmed,
            StartUtc = NextWeek.AddHours(9), EndUtc = NextWeek.AddHours(10), ExternalCalendarEventId = "g1"
        };
        db.CalendarItems.Add(item);
        db.ItemReminders.Add(new ItemReminder { CalendarItemId = item.Id, OffsetMinutes = 10 });
        await db.SaveChangesAsync();

        await UpdateAsync(db, item, [Before(99)], userId);

        Assert.Equal(99, Assert.Single(await RemindersOf(db, item.Id)).OffsetMinutes);
    }

    [Fact]
    public async Task Update_ATaskFromGoogleTasks_CanHaveItsOwnAlerts()
    {
        var (db, teamId, userId) = await SetUpAsync();
        var item = new CalendarItem
        {
            TeamId = teamId, AssignedUserId = userId, Title = "Write", TaskType = TaskType.Task, Status = TaskStatus.Backlog, IsAllDay = true,
            StartUtc = NextWeek, DeadlineUtc = NextWeek.AddDays(3), ExternalTaskId = "t1"
        };
        db.CalendarItems.Add(item);
        await db.SaveChangesAsync();

        await UpdateAsync(db, item, [DayBefore(1, 9 * 60, ReminderAnchor.Deadline)], userId);

        var reminder = Assert.Single(await RemindersOf(db, item.Id));
        Assert.Equal(ReminderAnchor.Deadline, reminder.Anchor);
    }

    [Fact]
    public async Task Update_ToAllDay_WithAnOffsetAlert_IsRefusedUntilTheAlertIsChanged()
    {
        var (db, teamId, userId) = await SetUpAsync();
        var created = await CreateAsync(db, NewEvent(teamId, userId, [Before(30)]));
        var item = await db.CalendarItems.SingleAsync(i => i.Id == created.Id);

        await Assert.ThrowsAsync<ValidationException>(() => UpdateAsync(db, item, [Before(30)], userId, allDay: true));
    }
}
