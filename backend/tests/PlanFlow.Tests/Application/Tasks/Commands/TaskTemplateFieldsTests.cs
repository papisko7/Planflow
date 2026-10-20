using Moq;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Application.Tasks.Commands.CreateTask;
using PlanFlow.Application.Tasks.Commands.UpdateTask;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Application.Tasks.Commands;

/// <summary>
/// What the template of an item allows (place, colour, description, all-day, busy/free) is enforced by the server
/// on create and update, and an item imported from Google keeps the fields Google owns.
/// </summary>
public class TaskTemplateFieldsTests
{
    private static ICacheService Cache => new Mock<ICacheService>().Object;

    private static readonly DateTime NextWeek = DateTime.UtcNow.Date.AddDays(7);

    private static async Task<(PlanFlowDbContext Db, Guid TeamId, Guid UserId)> SetUpAsync()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner));
        return (db, team.Id, owner.Id);
    }

    private static CreateTaskCommand NewEvent(
        Guid teamId, Guid userId, EventTemplate template, bool isAllDay = false, bool isBusy = true,
        string? description = "Notes", string? location = "Lisbon", string? colorKey = "tomato",
        DateTime? start = null, DateTime? end = null) =>
        new(teamId, "Item", description, null, null, 5, null, userId, TaskType.Event,
            StartUtc: start ?? NextWeek.AddHours(9), EndUtc: end ?? (start ?? NextWeek.AddHours(9)).AddHours(1),
            EventTemplate: template, IsAllDay: isAllDay, Location: location, ColorKey: colorKey, IsBusy: isBusy);

    private static async Task<PlanFlow.Application.Tasks.Common.TaskDto> CreateAsync(PlanFlowDbContext db, CreateTaskCommand command) =>
        await new CreateTaskCommandHandler(db, Cache, new NotificationService(db)).Handle(command, CancellationToken.None);

    // ---- create: the template decides which fields are kept ----

    [Fact]
    public async Task DefaultEvent_KeepsEveryField()
    {
        var (db, teamId, userId) = await SetUpAsync();

        var dto = await CreateAsync(db, NewEvent(teamId, userId, EventTemplate.Default, isBusy: false));

        Assert.Equal("Notes", dto.Description);
        Assert.Equal("Lisbon", dto.Location);
        Assert.Equal("tomato", dto.ColorKey);
        Assert.False(dto.IsBusy);
        Assert.Equal(EventTemplate.Default, dto.EventTemplate);
    }

    [Fact]
    public async Task OutOfOffice_KeepsColour_ButDropsDescriptionAndPlace()
    {
        var (db, teamId, userId) = await SetUpAsync();

        var dto = await CreateAsync(db, NewEvent(teamId, userId, EventTemplate.OutOfOffice));

        Assert.Equal(EventTemplate.OutOfOffice, dto.EventTemplate);
        Assert.Equal("tomato", dto.ColorKey);
        Assert.Null(dto.Description);
        Assert.Null(dto.Location);
        Assert.True(dto.IsBusy); // no busy/free choice for this template
    }

    [Fact]
    public async Task EffortIsKeptForAPlainEvent_ButDroppedForAnItemThatIsNotRanked()
    {
        var (db, teamId, userId) = await SetUpAsync();

        var plain = await CreateAsync(db, NewEvent(teamId, userId, EventTemplate.Default) with { EstimatedEffortHours = 2 });
        var dayOff = await CreateAsync(db, NewEvent(teamId, userId, EventTemplate.OutOfOffice) with { EstimatedEffortHours = 2 });

        Assert.Equal(2, plain.EstimatedEffortHours);
        Assert.Null(dayOff.EstimatedEffortHours);
    }

    [Fact]
    public async Task WorkingLocation_KeepsTheLabel_ButDropsColourAndDescription()
    {
        var (db, teamId, userId) = await SetUpAsync();

        var dto = await CreateAsync(db, NewEvent(teamId, userId, EventTemplate.WorkingLocation, location: "  Home  "));

        Assert.Equal("Home", dto.Location);
        Assert.Null(dto.ColorKey);
        Assert.Null(dto.Description);
    }

    [Fact]
    public async Task FocusTime_IsAlwaysBusy_AndNeverAllDay()
    {
        var (db, teamId, userId) = await SetUpAsync();

        var dto = await CreateAsync(db, NewEvent(teamId, userId, EventTemplate.FocusTime, isAllDay: true, isBusy: false));

        Assert.True(dto.IsBusy);
        Assert.False(dto.IsAllDay);
        Assert.Equal("Lisbon", dto.Location); // Focus time may have a place
    }

    [Fact]
    public async Task Birthday_IsAlwaysAllDay_AndEndsTheMidnightAfterItsDay()
    {
        var (db, teamId, userId) = await SetUpAsync();
        var day = NextWeek;

        var dto = await CreateAsync(db, new CreateTaskCommand(teamId, "Anna", null, null, null, null, null, userId, TaskType.Event,
            StartUtc: day, EventTemplate: EventTemplate.Birthday, ColorKey: "basil", Location: "Cake place"));

        Assert.True(dto.IsAllDay);
        Assert.Equal(day, dto.StartUtc);
        Assert.Equal(day.AddDays(1), dto.EndUtc);
        Assert.Null(dto.Location);
    }

    [Fact]
    public async Task AllDayEvent_WithoutEnd_LastsThatOneDay()
    {
        var (db, teamId, userId) = await SetUpAsync();

        var dto = await CreateAsync(db, new CreateTaskCommand(teamId, "Conference", null, null, null, 5, null, userId, TaskType.Event,
            StartUtc: NextWeek, IsAllDay: true));

        Assert.True(dto.IsAllDay);
        Assert.Equal(NextWeek.AddDays(1), dto.EndUtc);
    }

    [Fact]
    public async Task Task_IsAlwaysTheDefaultTemplate_NotAllDay_AndWithoutPlace()
    {
        var (db, teamId, userId) = await SetUpAsync();

        var dto = await CreateAsync(db, new CreateTaskCommand(teamId, "Report", "Notes", null, DateTime.UtcNow.AddDays(2), 5, null, userId,
            IsAllDay: true, Location: "Office", ColorKey: "grape"));

        Assert.Equal(EventTemplate.Default, dto.EventTemplate);
        Assert.False(dto.IsAllDay);
        Assert.Null(dto.Location);
        Assert.Equal("grape", dto.ColorKey); // a task can have a colour
        Assert.Equal("Notes", dto.Description);
    }

    // ---- update ----

    [Fact]
    public async Task Update_SwitchingToAnotherTemplate_DropsWhatTheNewOneDoesNotHave()
    {
        var (db, teamId, userId) = await SetUpAsync();
        var created = await CreateAsync(db, NewEvent(teamId, userId, EventTemplate.Default));

        var dto = await new UpdateTaskCommandHandler(db, Cache, new NotificationService(db)).Handle(
            new UpdateTaskCommand(created.Id, "Holiday", "Notes", TaskStatus.Confirmed, null, null, null, null, null, null, userId, TaskType.Event,
                StartUtc: NextWeek.AddHours(9), EndUtc: NextWeek.AddHours(10),
                EventTemplate: EventTemplate.OutOfOffice, Location: "Lisbon", ColorKey: "tomato"),
            CancellationToken.None);

        Assert.Equal(EventTemplate.OutOfOffice, dto.EventTemplate);
        Assert.Null(dto.Description);
        Assert.Null(dto.Location);
        Assert.Equal("tomato", dto.ColorKey);
    }

    [Fact]
    public async Task Update_ChangingAnEventIntoATask_ResetsTheTemplate()
    {
        var (db, teamId, userId) = await SetUpAsync();
        var created = await CreateAsync(db, NewEvent(teamId, userId, EventTemplate.OutOfOffice));

        var dto = await new UpdateTaskCommandHandler(db, Cache, new NotificationService(db)).Handle(
            new UpdateTaskCommand(created.Id, "Now a task", null, TaskStatus.Backlog, null, DateTime.UtcNow.AddDays(3), 5, null, null, null, userId,
                TaskType.Task, EventTemplate: EventTemplate.Default),
            CancellationToken.None);

        Assert.Equal(EventTemplate.Default, dto.EventTemplate);
        Assert.Null(dto.StartUtc);
        Assert.False(dto.IsAllDay);
    }

    [Fact]
    public async Task Update_OfAnImportedEvent_TakesEveryField_ButKeepsItsKind()
    {
        var (db, teamId, userId) = await SetUpAsync();
        var start = NextWeek.AddHours(9);
        var item = new CalendarItem
        {
            TeamId = teamId, Title = "Google title", Description = "Google notes", TaskType = TaskType.Event, Status = TaskStatus.Confirmed,
            EventTemplate = EventTemplate.Default, Location = "Google place", ColorKey = "sage", IsBusy = false,
            StartUtc = start, EndUtc = start.AddHours(1), ExternalCalendarEventId = "gcal-1", IsDataIncomplete = true
        };
        db.CalendarItems.Add(item);
        await db.SaveChangesAsync();

        var dto = await new UpdateTaskCommandHandler(db, Cache, new NotificationService(db)).Handle(
            new UpdateTaskCommand(item.Id, "My title", "My notes", TaskStatus.Tentative, null, null, 8, null, null, null, userId, TaskType.Event,
                StartUtc: start.AddDays(1), EndUtc: start.AddDays(1).AddHours(2), EstimatedEffortHours: 3,
                EventTemplate: EventTemplate.OutOfOffice, Location: "My place", ColorKey: "tomato", IsBusy: true),
            CancellationToken.None);

        // Editable in the app as well: taken.
        Assert.Equal("My title", dto.Title);
        Assert.Equal("My notes", dto.Description);
        Assert.Equal(start.AddDays(1), dto.StartUtc);
        Assert.Equal(start.AddDays(1).AddHours(2), dto.EndUtc);
        Assert.Equal("My place", dto.Location);
        Assert.Equal("tomato", dto.ColorKey);
        Assert.True(dto.IsBusy);
        // Its kind is Google's: the template in the request is ignored.
        Assert.Equal(EventTemplate.Default, dto.EventTemplate);
        Assert.Equal(TaskType.Event, dto.TaskType);
        // PlanFlow's: taken. Saving the form also confirms the data.
        Assert.Equal(8, dto.ImpactScore);
        Assert.Equal(3, dto.EstimatedEffortHours);
        Assert.Equal(TaskStatus.Tentative, dto.Status);
        Assert.False(dto.IsDataIncomplete);
        Assert.True(dto.IsExternal);
    }

    // ---- validation ----

    private static IEnumerable<string> Errors(CreateTaskCommand command) =>
        new CreateTaskCommandValidator().Validate(command).Errors.Select(e => e.PropertyName);

    [Fact]
    public void ATaskCannotUseAnEventTemplate()
    {
        var command = new CreateTaskCommand(Guid.NewGuid(), "T", null, null, DateTime.UtcNow.AddDays(1), 5, null, null,
            EventTemplate: EventTemplate.OutOfOffice);

        Assert.Contains("EventTemplate", Errors(command));
    }

    [Fact]
    public void AnUnknownTemplate_IsRejected()
    {
        Assert.Contains("EventTemplate", Errors(NewEvent(Guid.NewGuid(), Guid.NewGuid(), (EventTemplate)99)));
    }

    [Fact]
    public void AnUnknownColour_IsRejected_AndTheDefaultIsAllowed()
    {
        Assert.Contains("ColorKey", Errors(NewEvent(Guid.NewGuid(), Guid.NewGuid(), EventTemplate.Default, colorKey: "neon")));
        Assert.DoesNotContain("ColorKey", Errors(NewEvent(Guid.NewGuid(), Guid.NewGuid(), EventTemplate.Default, colorKey: null)));
    }

    [Fact]
    public void ATooLongPlace_IsRejected()
    {
        Assert.Contains("Location", Errors(NewEvent(Guid.NewGuid(), Guid.NewGuid(), EventTemplate.Default, location: new string('x', 513))));
    }

    [Fact]
    public void AnAllDayEvent_MustBeWholeDays()
    {
        var teamId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var valid = NewEvent(teamId, userId, EventTemplate.Default, isAllDay: true, start: NextWeek, end: NextWeek.AddDays(2));
        var timedStart = NewEvent(teamId, userId, EventTemplate.Default, isAllDay: true, start: NextWeek.AddHours(9), end: NextWeek.AddDays(2));
        var timedEnd = NewEvent(teamId, userId, EventTemplate.Default, isAllDay: true, start: NextWeek, end: NextWeek.AddDays(2).AddHours(5));
        var tooShort = NewEvent(teamId, userId, EventTemplate.Default, isAllDay: true, start: NextWeek, end: NextWeek);

        Assert.Empty(Errors(valid));
        Assert.Contains("StartUtc", Errors(timedStart));
        Assert.Contains("EndUtc", Errors(timedEnd));
        Assert.Contains("EndUtc", Errors(tooShort));
    }

    [Fact]
    public void ABirthday_MustBeWholeDays_EvenWithoutTheAllDayFlag()
    {
        var command = NewEvent(Guid.NewGuid(), Guid.NewGuid(), EventTemplate.Birthday, isAllDay: false, start: NextWeek.AddHours(9));

        Assert.Contains("StartUtc", Errors(command));
    }

    [Fact]
    public void FocusTime_IgnoresTheAllDayFlag_SoATimedSlotIsFine()
    {
        var command = NewEvent(Guid.NewGuid(), Guid.NewGuid(), EventTemplate.FocusTime, isAllDay: true, start: NextWeek.AddHours(9));

        Assert.Empty(Errors(command));
    }

    [Fact]
    public void ANewAllDayEvent_ThatIsOver_IsRejected_ButOneOnTodayIsNot()
    {
        var teamId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var today = DateTime.UtcNow.Date;

        Assert.Contains("EndUtc", Errors(NewEvent(teamId, userId, EventTemplate.Default, isAllDay: true, start: today.AddDays(-5), end: today.AddDays(-4))));
        Assert.Empty(Errors(NewEvent(teamId, userId, EventTemplate.Default, isAllDay: true, start: today, end: today.AddDays(1))));
    }
}
