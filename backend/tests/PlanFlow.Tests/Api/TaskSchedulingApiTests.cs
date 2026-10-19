using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;
using PlanFlow.Tests.Integration.PostgresIntegration;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Api;

/// <summary>
/// Step 7.1 through the real HTTP pipeline: clients that predate the scheduling fields must not wipe them on edit,
/// and dates sent without a zone or with an offset are understood as UTC instead of crashing with a 500.
/// </summary>
[Collection(PostgresCollection.Name)]
public class TaskSchedulingApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public TaskSchedulingApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, Guid TeamId)> OwnerWithTeamAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
        var user = new User { Email = $"{Guid.NewGuid():N}@example.com", DisplayName = "Owner", PasswordHash = "x", EmailVerifiedAtUtc = DateTime.UtcNow };
        var team = new Team { Name = "Scheduling team" };
        db.Users.Add(user);
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = user.Id, Role = TeamRole.Owner });
        await db.SaveChangesAsync();

        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwt.GenerateAccessToken(user, TeamRole.Owner, team.Id);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, team.Id);
    }

    private static string Iso(TimeSpan fromNow) => DateTime.UtcNow.Add(fromNow).ToString("yyyy-MM-ddTHH:mm:ss'Z'");

    [Fact]
    public async Task OldClientEdit_KeepsTheEventsStartEffortAndRecurrence()
    {
        var (client, teamId) = await OwnerWithTeamAsync();

        var created = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new
        {
            title = "Weekly sync", impactScore = 5, taskType = 1,
            startUtc = Iso(TimeSpan.FromHours(3)), endUtc = Iso(TimeSpan.FromHours(4)),
            estimatedEffortHours = 1.0, isRecurring = true, recurrenceFrequency = 1, recurrenceInterval = 1
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // The pre-7.1 mobile body: no scheduling fields at all.
        var edit = await client.PutAsJsonAsync($"api/tasks/{id}", new
        {
            title = "Weekly sync (renamed)", status = 0, deadlineUtc = Iso(TimeSpan.FromHours(4)), impactScore = 5
        });

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var body = await edit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("taskType").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("startUtc").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("endUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("deadlineUtc").ValueKind);
        Assert.Equal(1.0, body.GetProperty("estimatedEffortHours").GetDouble());
        Assert.True(body.GetProperty("isRecurring").GetBoolean());
        Assert.Equal(1, body.GetProperty("recurrenceFrequency").GetInt32());
        Assert.Equal(1, body.GetProperty("recurrenceInterval").GetInt32());
    }

    [Fact]
    public async Task NewClientEdit_CanStillClearTheSchedulingFields()
    {
        var (client, teamId) = await OwnerWithTeamAsync();

        var created = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new
        {
            title = "Report", impactScore = 5, deadlineUtc = Iso(TimeSpan.FromDays(3)), estimatedEffortHours = 6.0
        });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var edit = await client.PutAsJsonAsync($"api/tasks/{id}", new
        {
            title = "Report", status = 0, deadlineUtc = Iso(TimeSpan.FromDays(3)), impactScore = 5,
            taskType = 0, estimatedEffortHours = (double?)null, isRecurring = false
        });

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var body = await edit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("estimatedEffortHours").ValueKind);
    }

    [Fact]
    public async Task EventThatHasEnded_DropsToZero_ButAnOverdueTaskStaysAtTheMaximum()
    {
        var (client, teamId) = await OwnerWithTeamAsync();

        async Task<Guid> CreateAsync(object body)
        {
            var response = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", body);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }

        var eventId = await CreateAsync(new { title = "Meeting", impactScore = 8, taskType = 1, startUtc = Iso(TimeSpan.FromHours(3)), endUtc = Iso(TimeSpan.FromHours(4)) });
        var taskId = await CreateAsync(new { title = "Report", impactScore = 8, deadlineUtc = Iso(TimeSpan.FromDays(2)) });

        // Time passes: both are moved into the past (editing the past is allowed, creating it is not).
        var pastEvent = await client.PutAsJsonAsync($"api/tasks/{eventId}", new
        {
            title = "Meeting", status = 0, impactScore = 8, taskType = 1,
            startUtc = Iso(TimeSpan.FromHours(-5)), endUtc = Iso(TimeSpan.FromHours(-4))
        });
        var overdueTask = await client.PutAsJsonAsync($"api/tasks/{taskId}", new
        {
            title = "Report", status = 0, impactScore = 8, taskType = 0, deadlineUtc = Iso(TimeSpan.FromDays(-3))
        });

        Assert.Equal(HttpStatusCode.OK, pastEvent.StatusCode);
        Assert.Equal(HttpStatusCode.OK, overdueTask.StatusCode);
        Assert.Equal(0.0, (await pastEvent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("currentUrgencyScore").GetDouble());
        Assert.True((await overdueTask.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("currentUrgencyScore").GetDouble() >= 0.85);
    }

    [Fact]
    public async Task ReasonText_IsExposedInTheTeamListAndTheDetail()
    {
        var (client, teamId) = await OwnerWithTeamAsync();
        var created = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks",
            new { title = "Explain me", impactScore = 8, deadlineUtc = Iso(TimeSpan.FromDays(2)) });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var list = await client.GetFromJsonAsync<JsonElement>($"api/teams/{teamId}/tasks");
        var listed = list.EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == id);
        var detail = await client.GetFromJsonAsync<JsonElement>($"api/tasks/{id}");

        var listReason = listed.GetProperty("reasonText").GetString();
        Assert.StartsWith("[", listReason);
        Assert.Contains("impact 8/10", listReason);
        Assert.Equal(listReason, detail.GetProperty("latestScoreBreakdown").GetProperty("reasonText").GetString());
    }

    [Fact]
    public async Task ImportedGoogleFields_AreExposedInTheListAndTheDetail_AndSurviveAnEditInTheApp()
    {
        var (client, teamId) = await OwnerWithTeamAsync();
        var firstDay = DateTime.UtcNow.Date.AddDays(5); // an all-day item is stored as whole days (midnight UTC)
        Guid id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
            var item = new CalendarItem
            {
                TeamId = teamId, Title = "Holiday", TaskType = TaskType.Event, Status = TaskStatus.Confirmed,
                EventTemplate = EventTemplate.Default, IsAllDay = true, ExternalCalendarEventId = "gcal-1", Location = "Lisbon", ColorKey = "tomato", IsBusy = false,
                ExternalSettings = "Auto-decline: declines all conflicting invitations",
                StartUtc = firstDay, EndUtc = firstDay.AddDays(2)
            };
            item.Reminders.Add(ItemReminder.Day(1, 540));
            item.Reminders.Add(ItemReminder.Offset(30));
            db.CalendarItems.Add(item);
            await db.SaveChangesAsync();
            id = item.Id;
        }

        var detail = await client.GetFromJsonAsync<JsonElement>($"api/tasks/{id}");
        var task = detail.GetProperty("task");
        Assert.Equal((int)EventTemplate.Default, task.GetProperty("eventTemplate").GetInt32());
        Assert.True(task.GetProperty("isExternal").GetBoolean());
        Assert.True(task.GetProperty("isAllDay").GetBoolean());
        Assert.Equal("Lisbon", task.GetProperty("location").GetString());
        Assert.Equal("tomato", task.GetProperty("colorKey").GetString());
        Assert.False(task.GetProperty("isBusy").GetBoolean());
        Assert.Equal("Auto-decline: declines all conflicting invitations", task.GetProperty("externalSettings").GetString());
        var reminders = detail.GetProperty("reminders").EnumerateArray().ToList();
        Assert.Equal(2, reminders.Count);
        Assert.Contains(reminders, r => r.GetProperty("daysBefore").ValueKind == JsonValueKind.Number
            && r.GetProperty("daysBefore").GetInt32() == 1 && r.GetProperty("timeOfDayMinutes").GetInt32() == 540);
        Assert.Contains(reminders, r => r.GetProperty("offsetMinutes").ValueKind == JsonValueKind.Number
            && r.GetProperty("offsetMinutes").GetInt32() == 30);

        var listed = (await client.GetFromJsonAsync<JsonElement>($"api/teams/{teamId}/tasks"))
            .EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == id);
        Assert.True(listed.GetProperty("isAllDay").GetBoolean());
        Assert.Equal("tomato", listed.GetProperty("colorKey").GetString());

        // Editing the item in the app (a different title and impact) must not wipe what came from Google.
        var edit = await client.PutAsJsonAsync($"api/tasks/{id}", new
        {
            title = "Holiday (renamed)", status = 5, impactScore = 3, taskType = 1,
            startUtc = firstDay, endUtc = firstDay.AddDays(2)
        });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var after = (await client.GetFromJsonAsync<JsonElement>($"api/tasks/{id}")).GetProperty("task");
        Assert.Equal("Lisbon", after.GetProperty("location").GetString());
        Assert.Equal("tomato", after.GetProperty("colorKey").GetString());
        Assert.True(after.GetProperty("isAllDay").GetBoolean());
        Assert.Equal((int)EventTemplate.Default, after.GetProperty("eventTemplate").GetInt32());
    }

    [Fact]
    public async Task TemplateFields_RoundTripThroughCreateAndEdit_AndAnOldClientEditKeepsThem()
    {
        var (client, teamId) = await OwnerWithTeamAsync();
        var firstDay = DateTime.UtcNow.Date.AddDays(10);

        var created = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new
        {
            title = "Conference", description = "Bring badge", impactScore = 6, taskType = 1,
            startUtc = firstDay, endUtc = firstDay.AddDays(2), isAllDay = true, eventTemplate = (int)EventTemplate.Default,
            location = "Warsaw", colorKey = "peacock", isBusy = false
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = dto.GetProperty("id").GetGuid();
        Assert.True(dto.GetProperty("isAllDay").GetBoolean());
        Assert.Equal("Warsaw", dto.GetProperty("location").GetString());
        Assert.Equal("peacock", dto.GetProperty("colorKey").GetString());
        Assert.False(dto.GetProperty("isBusy").GetBoolean());
        Assert.False(dto.GetProperty("isExternal").GetBoolean());

        // A client from before the template fields (no eventTemplate in the body) must not reset them.
        var oldClient = await client.PutAsJsonAsync($"api/tasks/{id}", new
        {
            title = "Conference 2", status = 5, impactScore = 6, taskType = 1, startUtc = firstDay, endUtc = firstDay.AddDays(2)
        });
        Assert.Equal(HttpStatusCode.OK, oldClient.StatusCode);
        var kept = await oldClient.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Warsaw", kept.GetProperty("location").GetString());
        Assert.Equal("peacock", kept.GetProperty("colorKey").GetString());
        Assert.True(kept.GetProperty("isAllDay").GetBoolean());
        Assert.False(kept.GetProperty("isBusy").GetBoolean());

        // A current client sends the whole block and changes it.
        var edited = await client.PutAsJsonAsync($"api/tasks/{id}", new
        {
            title = "Holiday", status = 5, impactScore = (int?)null, taskType = 1, startUtc = firstDay, endUtc = firstDay.AddDays(2),
            isAllDay = true, eventTemplate = (int)EventTemplate.OutOfOffice, colorKey = "grape"
        });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var changed = await edited.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)EventTemplate.OutOfOffice, changed.GetProperty("eventTemplate").GetInt32());
        Assert.Equal(JsonValueKind.Null, changed.GetProperty("location").ValueKind);
        Assert.Equal("grape", changed.GetProperty("colorKey").GetString());
    }

    [Fact]
    public async Task InvalidTemplateFields_AreRejectedWith400()
    {
        var (client, teamId) = await OwnerWithTeamAsync();
        var day = DateTime.UtcNow.Date.AddDays(10);

        var unknownColour = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new { title = "X", impactScore = 5, deadlineUtc = Iso(TimeSpan.FromDays(2)), colorKey = "neon" });
        var taskWithTemplate = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new { title = "X", impactScore = 5, deadlineUtc = Iso(TimeSpan.FromDays(2)), eventTemplate = (int)EventTemplate.FocusTime });
        var timedAllDay = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new { title = "X", impactScore = 5, taskType = 1, isAllDay = true, startUtc = day.AddHours(9), endUtc = day.AddDays(1) });

        Assert.Equal(HttpStatusCode.BadRequest, unknownColour.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, taskWithTemplate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, timedAllDay.StatusCode);
    }

    [Fact]
    public async Task InformationalItem_HasNoScoreAndNoReason_AndTurningATaskIntoOneResetsIt()
    {
        var (client, teamId) = await OwnerWithTeamAsync();

        // A task due very soon is critical...
        var created = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks",
            new { title = "Leave soon", impactScore = 9, deadlineUtc = Iso(TimeSpan.FromHours(3)) });
        var dto = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = dto.GetProperty("id").GetGuid();
        Assert.True(dto.GetProperty("currentUrgencyScore").GetDouble() >= 0.85);

        // ...but the same item turned into an Out of office is not a decision: no score, no reason, no breakdown.
        var start = DateTime.UtcNow.Date.AddDays(1);
        var edited = await client.PutAsJsonAsync($"api/tasks/{id}", new
        {
            title = "Leave soon", status = 5, taskType = 1, eventTemplate = (int)EventTemplate.OutOfOffice,
            startUtc = start, endUtc = start.AddDays(1), isAllDay = true
        });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(0.0, (await edited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("currentUrgencyScore").GetDouble());

        var list = await client.GetFromJsonAsync<JsonElement>($"api/teams/{teamId}/tasks");
        var listed = list.EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == id);
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("reasonText").ValueKind);

        var detail = await client.GetFromJsonAsync<JsonElement>($"api/tasks/{id}");
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("latestScoreBreakdown").ValueKind);
    }

    [Fact]
    public async Task ATasksPlannedStart_RoundTrips_FeedsTheBreakdown_AndMustNotBeAfterTheDeadline()
    {
        var (client, teamId) = await OwnerWithTeamAsync();

        var created = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new
        {
            title = "Plan the thesis", impactScore = 6, deadlineUtc = Iso(TimeSpan.FromDays(10)), startUtc = Iso(TimeSpan.FromDays(2))
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = dto.GetProperty("id").GetGuid();
        Assert.NotEqual(JsonValueKind.Null, dto.GetProperty("startUtc").ValueKind);

        var detail = await client.GetFromJsonAsync<JsonElement>($"api/tasks/{id}");
        var breakdown = detail.GetProperty("latestScoreBreakdown");
        Assert.True(breakdown.GetProperty("scheduledComponent").GetDouble() > 0.0);
        Assert.Equal(2, breakdown.GetProperty("algorithmVersion").GetInt32());
        Assert.Contains("planned start in", breakdown.GetProperty("reasonText").GetString());

        // An old client (no startUtc in the body, no taskType) must not wipe the planned start.
        var oldEdit = await client.PutAsJsonAsync($"api/tasks/{id}", new
        {
            title = "Plan the thesis v2", status = 0, deadlineUtc = Iso(TimeSpan.FromDays(10)), impactScore = 6
        });
        Assert.Equal(HttpStatusCode.OK, oldEdit.StatusCode);

        var late = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new
        {
            title = "Backwards", impactScore = 5, deadlineUtc = Iso(TimeSpan.FromDays(2)), startUtc = Iso(TimeSpan.FromDays(5))
        });
        Assert.Equal(HttpStatusCode.BadRequest, late.StatusCode);
    }

    [Fact]
    public async Task EditingAnImportedEvent_ChangesOnlyPlanflowFields()
    {
        var (client, teamId) = await OwnerWithTeamAsync();
        var start = DateTime.UtcNow.Date.AddDays(4).AddHours(9);
        Guid id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
            var item = new CalendarItem
            {
                TeamId = teamId, Title = "Google title", Description = "Google notes", TaskType = TaskType.Event, Status = TaskStatus.Confirmed,
                Location = "Google place", ColorKey = "sage", StartUtc = start, EndUtc = start.AddHours(1), ExternalCalendarEventId = "gcal-2"
            };
            db.CalendarItems.Add(item);
            await db.SaveChangesAsync();
            id = item.Id;
        }

        var response = await client.PutAsJsonAsync($"api/tasks/{id}", new
        {
            title = "Mine", description = "Mine", status = 6, impactScore = 9, taskType = 1, estimatedEffortHours = 2.0,
            startUtc = start.AddDays(1), endUtc = start.AddDays(1).AddHours(1), eventTemplate = (int)EventTemplate.Default,
            location = "Mine", colorKey = "tomato"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = (await client.GetFromJsonAsync<JsonElement>($"api/tasks/{id}")).GetProperty("task");
        Assert.Equal("Google title", after.GetProperty("title").GetString());
        Assert.Equal("Google notes", after.GetProperty("description").GetString());
        Assert.Equal("Google place", after.GetProperty("location").GetString());
        Assert.Equal("sage", after.GetProperty("colorKey").GetString());
        Assert.Equal(start, after.GetProperty("startUtc").GetDateTime().ToUniversalTime());
        Assert.Equal(9, after.GetProperty("impactScore").GetInt32());
        Assert.Equal(2.0, after.GetProperty("estimatedEffortHours").GetDouble());
        Assert.Equal((int)TaskStatus.Tentative, after.GetProperty("status").GetInt32());
    }

    // Step 7.5.7 through the real pipeline: a repeating event gets its occurrences, "scope" travels as a number in the body and in the query.
    [Fact]
    public async Task RepeatingEvent_EditAndDeleteWithAScope_ReachTheRightOccurrences()
    {
        var (client, teamId) = await OwnerWithTeamAsync();
        var start = DateTime.UtcNow.Date.AddDays(1).AddHours(9);
        string At(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ss'Z'");

        var created = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new
        {
            title = "Standup", impactScore = 5, taskType = 1, startUtc = At(start), endUtc = At(start.AddMinutes(30)),
            isRecurring = true, recurrenceFrequency = 0, recurrenceInterval = 1
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var first = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = first.GetProperty("id").GetGuid();
        Assert.Equal(0, first.GetProperty("recurrenceFrequency").GetInt32());
        var seriesId = first.GetProperty("seriesId").GetString();

        async Task<List<JsonElement>> SeriesAsync() =>
            (await client.GetFromJsonAsync<List<JsonElement>>($"api/teams/{teamId}/tasks"))!
                .Where(t => t.GetProperty("seriesId").GetString() == seriesId).ToList();
        Assert.InRange((await SeriesAsync()).Count, 88, 92);

        var edit = await client.PutAsJsonAsync($"api/tasks/{id}", new
        {
            title = "Daily sync", status = 5, impactScore = 5, taskType = 1, startUtc = At(start), endUtc = At(start.AddMinutes(30)),
            isRecurring = true, recurrenceFrequency = 0, recurrenceInterval = 1, scope = 2
        });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.All(await SeriesAsync(), t => Assert.Equal("Daily sync", t.GetProperty("title").GetString()));

        var delete = await client.DeleteAsync($"api/tasks/{id}?scope=2");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty(await SeriesAsync());
    }

    [Fact]
    public async Task Delete_WithAnUnknownScope_IsRejected()
    {
        var (client, teamId) = await OwnerWithTeamAsync();
        var created = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new { title = "Report", impactScore = 5, deadlineUtc = Iso(TimeSpan.FromDays(3)) });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var delete = await client.DeleteAsync($"api/tasks/{id}?scope=7");

        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
    }

    [Theory]
    [InlineData(false)] // 2026-10-19T11:00:00      (no zone)
    [InlineData(true)]  // 2026-10-19T13:00:00+02:00 (offset)
    public async Task DatesWithoutZoneOrWithOffset_AreReadAsUtc_NotA500(bool withOffset)
    {
        var (client, teamId) = await OwnerWithTeamAsync();
        var target = DateTime.UtcNow.AddDays(2);
        var text = withOffset
            ? target.AddHours(2).ToString("yyyy-MM-ddTHH:mm:ss") + "+02:00"
            : target.ToString("yyyy-MM-ddTHH:mm:ss");

        var response = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new { title = "Zone", impactScore = 5, deadlineUtc = text });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("deadlineUtc").GetDateTime();
        Assert.True(Math.Abs((stored.ToUniversalTime() - target).TotalSeconds) < 2);
    }
}
