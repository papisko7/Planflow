using PlanFlow.Application.Availability.Queries.GetMemberCalendar;
using PlanFlow.Application.Availability.Queries.GetTeamAvailability;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Users.Commands.UpdateCalendarSharing;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;
using PlanFlow.Infrastructure.Persistence;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Application.Availability;

/// <summary>
/// The team availability grid and a member's calendar. The server trims a member's personal calendar by their own sharing
/// level before anything leaves it; only members of the team may ask; work is counted from this team's tasks only.
/// </summary>
public class TeamAvailabilityTests
{
    private sealed record Setup(PlanFlowDbContext Db, Team Team, User Owner, User Anna, Team AnnaPersonal);

    private static async Task<Setup> SetUpAsync(CalendarSharingLevel annaLevel = CalendarSharingLevel.Availability)
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db, "Olga");
        var anna = await TestDb.AddUserAsync(db, "Anna");
        anna.CalendarSharingLevel = annaLevel;
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (anna, TeamRole.Member));
        var personal = new Team { Name = "Personal", IsPersonal = true };
        db.Teams.Add(personal);
        db.TeamMembers.Add(new TeamMember { TeamId = personal.Id, UserId = anna.Id, Role = TeamRole.Owner });
        await db.SaveChangesAsync();
        return new Setup(db, team, owner, anna, personal);
    }

    private static CalendarItem ImportedEvent(
        Setup s, string title, DateTime start, DateTime end, bool? isPrivate = false, EventTemplate template = EventTemplate.Default,
        string? location = "Clinic")
    {
        var item = new CalendarItem
        {
            TeamId = s.AnnaPersonal.Id, AssignedUserId = s.Anna.Id, Title = title, TaskType = TaskType.Event, Status = TaskStatus.Confirmed,
            EventTemplate = template, StartUtc = start, EndUtc = end, IsPrivate = isPrivate, Location = location, IsBusy = true,
            ExternalCalendarEventId = Guid.NewGuid().ToString()
        };
        s.Db.CalendarItems.Add(item);
        s.Db.SaveChanges();
        return item;
    }

    private static CalendarItem TeamTask(Setup s, User? assignee, double? effortHours, DateTime deadline, Team? team = null)
    {
        var item = new CalendarItem
        {
            TeamId = (team ?? s.Team).Id, AssignedUserId = assignee?.Id, Title = "Report", TaskType = TaskType.Task, Status = TaskStatus.Backlog,
            DeadlineUtc = deadline, EstimatedEffortHours = effortHours, ImpactScore = 5
        };
        s.Db.CalendarItems.Add(item);
        s.Db.SaveChanges();
        return item;
    }

    private static Task<PlanFlow.Application.Availability.Common.MemberCalendarDto> Calendar(Setup s, User asker, User member, int days = 7) =>
        new GetMemberCalendarQueryHandler(s.Db)
            .Handle(new GetMemberCalendarQuery(s.Team.Id, member.Id, asker.Id, DateTime.UtcNow.Date, days), CancellationToken.None);

    private static async Task<PlanFlow.Application.Availability.Common.MemberAvailabilityDto> RowOf(Setup s, User asker, User member)
    {
        var grid = await new GetTeamAvailabilityQueryHandler(s.Db)
            .Handle(new GetTeamAvailabilityQuery(s.Team.Id, asker.Id, DateTime.UtcNow.Date, 7), CancellationToken.None);
        return grid.Members.Single(m => m.UserId == member.Id);
    }

    [Fact]
    public async Task Grid_ListsEveryMemberWithTheirRole()
    {
        var s = await SetUpAsync();

        var grid = await new GetTeamAvailabilityQueryHandler(s.Db)
            .Handle(new GetTeamAvailabilityQuery(s.Team.Id, s.Owner.Id, DateTime.UtcNow.Date, 7), CancellationToken.None);

        Assert.Equal(2, grid.Members.Count);
        Assert.All(grid.Members, m => Assert.Equal(7, m.Days.Count));
        Assert.Equal(TeamRole.Member, grid.Members.Single(m => m.UserId == s.Anna.Id).Role);
    }

    [Fact]
    public async Task NonMember_CannotSeeTheGridOrACalendar()
    {
        var s = await SetUpAsync();
        var stranger = await TestDb.AddUserAsync(s.Db, "Eve");

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => new GetTeamAvailabilityQueryHandler(s.Db)
            .Handle(new GetTeamAvailabilityQuery(s.Team.Id, stranger.Id, DateTime.UtcNow.Date, 7), CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Calendar(s, stranger, s.Anna));
    }

    [Fact]
    public async Task CalendarOfSomeoneOutsideTheTeam_IsNotFound()
    {
        var s = await SetUpAsync();
        var stranger = await TestDb.AddUserAsync(s.Db, "Eve");

        await Assert.ThrowsAsync<NotFoundException>(() => Calendar(s, s.Owner, stranger));
    }

    [Fact]
    public async Task Off_ShowsNoData_AndNoPersonalEntries()
    {
        var s = await SetUpAsync(CalendarSharingLevel.Off);
        ImportedEvent(s, "Therapy", DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow.AddMinutes(50));

        var row = await RowOf(s, s.Owner, s.Anna);
        var calendar = await Calendar(s, s.Owner, s.Anna);

        Assert.Equal(MemberStatus.NoData, row.CurrentStatus);
        Assert.All(row.Days, d => Assert.Equal(DayAvailability.NoData, d.Availability));
        Assert.Empty(calendar.Entries);
    }

    [Fact]
    public async Task Availability_ShowsBusyNow_WithoutTitleOrPlace()
    {
        var s = await SetUpAsync();
        ImportedEvent(s, "Therapy", DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow.AddMinutes(50));

        var row = await RowOf(s, s.Owner, s.Anna);
        var entry = Assert.Single((await Calendar(s, s.Owner, s.Anna)).Entries);

        Assert.Equal(MemberStatus.Busy, row.CurrentStatus);
        Assert.Null(entry.Title);
        Assert.Null(entry.Location);
        Assert.Equal("Personal", entry.Source);
    }

    [Fact]
    public async Task Details_ShowsTitleAndPlace_ButAPrivateEventStaysBusy()
    {
        var s = await SetUpAsync(CalendarSharingLevel.Details);
        ImportedEvent(s, "Therapy", DateTime.UtcNow.AddHours(2), DateTime.UtcNow.AddHours(3), isPrivate: false);
        ImportedEvent(s, "Job interview", DateTime.UtcNow.AddHours(4), DateTime.UtcNow.AddHours(5), isPrivate: true);

        var entries = (await Calendar(s, s.Owner, s.Anna)).Entries;

        Assert.Equal("Therapy", entries.Single(e => e.Kind == BlockKind.Busy && e.Title is not null).Title);
        var hidden = entries.Single(e => e.Title is null);
        Assert.Equal(BlockKind.Busy, hidden.Kind);
        Assert.Null(hidden.Location);
        Assert.DoesNotContain(entries, e => e.Title == "Job interview");
    }

    [Fact]
    public async Task EveryRole_SeesTheSameThing()
    {
        var s = await SetUpAsync(CalendarSharingLevel.Details);
        var ben = await TestDb.AddUserAsync(s.Db, "Ben");
        s.Db.TeamMembers.Add(new TeamMember { TeamId = s.Team.Id, UserId = ben.Id, Role = TeamRole.Member });
        await s.Db.SaveChangesAsync();
        ImportedEvent(s, "Therapy", DateTime.UtcNow.AddHours(2), DateTime.UtcNow.AddHours(3));

        var asOwner = await Calendar(s, s.Owner, s.Anna);
        var asMember = await Calendar(s, ben, s.Anna);

        Assert.Equal(asOwner.Entries.Select(e => e.Title), asMember.Entries.Select(e => e.Title));
    }

    [Fact]
    public async Task OutOfOffice_Today_ShowsAsOutOfOffice_AndRemovesCapacity()
    {
        var s = await SetUpAsync();
        var today = DateTime.UtcNow.Date;
        var absence = ImportedEvent(s, "Holiday", today, today.AddDays(2), template: EventTemplate.OutOfOffice);
        absence.IsAllDay = true;
        await s.Db.SaveChangesAsync();

        var row = await RowOf(s, s.Owner, s.Anna);

        Assert.Equal(MemberStatus.OutOfOffice, row.CurrentStatus);
        Assert.Equal(DayAvailability.OutOfOffice, row.Days[0].Availability);
        Assert.Equal(DayAvailability.OutOfOffice, row.Days[1].Availability);
        Assert.Equal(0, row.Days[0].CapacityHours);
    }

    [Fact]
    public async Task Load_CountsOnlyThisTeamsTasks()
    {
        var s = await SetUpAsync();
        var otherTeam = await TestDb.AddTeamAsync(s.Db, (s.Anna, TeamRole.Owner));
        var deadline = DateTime.UtcNow.Date.AddDays(14);
        TeamTask(s, s.Anna, 10, deadline);
        TeamTask(s, s.Anna, 500, deadline, otherTeam);

        var row = await RowOf(s, s.Owner, s.Anna);

        Assert.Equal(1, row.OpenTasks);
    }

    [Fact]
    public async Task Load_SharedTaskEffort_IsSplitEquallyWithCoContractors()
    {
        var s = await SetUpAsync();
        var task = TeamTask(s, s.Owner, 10, DateTime.UtcNow.Date.AddDays(60));
        s.Db.ItemParticipants.Add(new ItemParticipant { CalendarItemId = task.Id, UserId = s.Anna.Id, Role = ParticipantRole.CoContractor });
        await s.Db.SaveChangesAsync();

        var owner = await RowOf(s, s.Owner, s.Owner);
        var anna = await RowOf(s, s.Owner, s.Anna);

        Assert.Equal(1, anna.OpenTasks);
        Assert.Equal(owner.WindowLoadPercent, anna.WindowLoadPercent);
        Assert.True(anna.WindowLoadPercent > 0);
    }

    [Fact]
    public async Task Load_TaskWithoutEffort_IsCountedAsUnknown_NotAsZeroLoad()
    {
        var s = await SetUpAsync();
        TeamTask(s, s.Anna, null, DateTime.UtcNow.Date.AddDays(3));

        var row = await RowOf(s, s.Owner, s.Anna);

        Assert.Equal(1, row.OpenTasks);
        Assert.Equal(1, row.TasksWithoutEffort);
    }

    [Fact]
    public async Task Load_TooMuchWorkForTheWindow_IsOverloaded()
    {
        var s = await SetUpAsync();
        TeamTask(s, s.Anna, 200, DateTime.UtcNow.Date.AddDays(2));

        var row = await RowOf(s, s.Owner, s.Anna);

        Assert.Equal(LoadLevel.Overloaded, row.LoadLevel);
    }

    [Fact]
    public async Task MemberCalendar_ListsOpenTasksOfThatMemberOnly()
    {
        var s = await SetUpAsync();
        TeamTask(s, s.Anna, 2, DateTime.UtcNow.Date.AddDays(3));
        TeamTask(s, s.Owner, 2, DateTime.UtcNow.Date.AddDays(3));

        var calendar = await Calendar(s, s.Owner, s.Anna);

        Assert.Single(calendar.Tasks);
    }

    private static Task<PlanFlow.Application.Availability.Common.MemberCalendarDto> CalendarFrom(Setup s, User asker, User member, DateTime from, int days) =>
        new GetMemberCalendarQueryHandler(s.Db)
            .Handle(new GetMemberCalendarQuery(s.Team.Id, member.Id, asker.Id, from, days), CancellationToken.None);

    private static CalendarItem FinishedTask(Setup s, User assignee, DateTime deadline)
    {
        var item = TeamTask(s, assignee, 2, deadline);
        item.Status = TaskStatus.Completed;
        s.Db.SaveChanges();
        return item;
    }

    [Fact]
    public async Task MemberCalendar_ListsFinishedTasksOfTheWindow_NotOpenOnesAndNotOtherDays()
    {
        var s = await SetUpAsync();
        var first = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var done = FinishedTask(s, s.Anna, first.AddDays(9));
        FinishedTask(s, s.Anna, first.AddDays(40));
        FinishedTask(s, s.Owner, first.AddDays(9));
        TeamTask(s, s.Anna, 2, first.AddDays(12));

        var calendar = await CalendarFrom(s, s.Owner, s.Anna, first, 30);

        Assert.Equal(done.Id, Assert.Single(calendar.FinishedTasks).ItemId);
        Assert.Equal(TaskStatus.Completed, calendar.FinishedTasks[0].Status);
        Assert.Single(calendar.Tasks);
    }

    [Fact]
    public async Task MemberCalendar_FinishedTasksOfACoContractor_AreListedToo()
    {
        var s = await SetUpAsync();
        var first = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var shared = FinishedTask(s, s.Owner, first.AddDays(3));
        s.Db.ItemParticipants.Add(new ItemParticipant { CalendarItemId = shared.Id, UserId = s.Anna.Id, Role = ParticipantRole.CoContractor });
        await s.Db.SaveChangesAsync();

        var calendar = await CalendarFrom(s, s.Owner, s.Anna, first, 30);

        Assert.Equal(shared.Id, Assert.Single(calendar.FinishedTasks).ItemId);
    }

    [Fact]
    public async Task MemberCalendar_FinishedTasksAreTeamData_SoTheyShowEvenWhenThePersonSharesNothing()
    {
        var s = await SetUpAsync(CalendarSharingLevel.Off);
        var first = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        FinishedTask(s, s.Anna, first.AddDays(5));
        ImportedEvent(s, "Therapy", first.AddDays(5).AddHours(10), first.AddDays(5).AddHours(11));

        var calendar = await CalendarFrom(s, s.Owner, s.Anna, first, 30);

        Assert.Single(calendar.FinishedTasks);
        Assert.Empty(calendar.Entries);
    }

    [Fact]
    public async Task MemberCalendar_AWindowInThePast_ReturnsItsEventsAndKeepsTheLoadAtZero()
    {
        var s = await SetUpAsync(CalendarSharingLevel.Details);
        var past = DateTime.UtcNow.Date.AddDays(-60);
        ImportedEvent(s, "Old meeting", past.AddHours(9), past.AddHours(10));
        TeamTask(s, s.Anna, 4, DateTime.UtcNow.Date.AddDays(5));

        var calendar = await CalendarFrom(s, s.Owner, s.Anna, past.AddDays(-3), 10);

        Assert.Equal("Old meeting", Assert.Single(calendar.Entries).Title);
        Assert.All(calendar.Member.Days, d => Assert.Equal(0, d.LoadHours));
    }

    [Fact]
    public async Task AnotherUsersImportedEvent_NeverShowsInAMembersCalendar()
    {
        var s = await SetUpAsync(CalendarSharingLevel.Details);
        var olgaPersonal = new Team { Name = "Personal", IsPersonal = true };
        s.Db.Teams.Add(olgaPersonal);
        s.Db.CalendarItems.Add(new CalendarItem
        {
            TeamId = olgaPersonal.Id, AssignedUserId = s.Owner.Id, Title = "Olga secret", TaskType = TaskType.Event, IsPrivate = false,
            StartUtc = DateTime.UtcNow.AddHours(1), EndUtc = DateTime.UtcNow.AddHours(2), ExternalCalendarEventId = "x"
        });
        await s.Db.SaveChangesAsync();

        var calendar = await Calendar(s, s.Owner, s.Anna);

        Assert.Empty(calendar.Entries);
    }

    // ----- UpdateCalendarSharing -----

    [Fact]
    public async Task UpdateSharing_StoresTheLevel()
    {
        var s = await SetUpAsync();

        await new UpdateCalendarSharingCommandHandler(s.Db)
            .Handle(new UpdateCalendarSharingCommand(s.Anna.Id, CalendarSharingLevel.Details), CancellationToken.None);

        Assert.Equal(CalendarSharingLevel.Details, s.Db.Users.Single(u => u.Id == s.Anna.Id).CalendarSharingLevel);
    }

    [Fact]
    public void UpdateSharing_RejectsAnUnknownLevel()
    {
        var result = new UpdateCalendarSharingCommandValidator()
            .Validate(new UpdateCalendarSharingCommand(Guid.NewGuid(), (CalendarSharingLevel)9));

        Assert.False(result.IsValid);
    }
}
