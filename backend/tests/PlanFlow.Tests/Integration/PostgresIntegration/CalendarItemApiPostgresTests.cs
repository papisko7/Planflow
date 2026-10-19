using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Integration.PostgresIntegration;

/// <summary>
/// Step 7.8: creating and editing items with the fields added in the thesis-improvements phase (type and times, effort, place, colour,
/// busy/free, alerts, guests, repeat rule with a scope), through the real HTTP pipeline onto a real PostgreSQL database. The same
/// endpoints are exercised on InMemory in <c>Api/*ApiTests</c>; here the SQL, the foreign-key cascades and the column widths are real.
/// </summary>
[Collection(PostgresCollection.Name)]
public class CalendarItemApiPostgresTests
{
    private readonly PostgresWebApplicationFactory _factory;

    public CalendarItemApiPostgresTests(PostgresWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record Scene(HttpClient Owner, Guid TeamId, Guid OwnerId, Guid BenId);

    private async Task<Scene> SceneAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
        var team = new Team { Name = "Items team" };
        User NewUser(string name) => new() { Email = $"{Guid.NewGuid():N}@example.com", DisplayName = name, PasswordHash = "x", EmailVerifiedAtUtc = DateTime.UtcNow };
        var owner = NewUser("Owner");
        var ben = NewUser("Ben");
        db.Teams.Add(team);
        db.Users.AddRange(owner, ben);
        db.TeamMembers.AddRange(
            new TeamMember { TeamId = team.Id, UserId = owner.Id, Role = TeamRole.Owner },
            new TeamMember { TeamId = team.Id, UserId = ben.Id, Role = TeamRole.Member });
        await db.SaveChangesAsync();

        var (token, _) = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().GenerateAccessToken(owner, TeamRole.Owner, team.Id);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new Scene(client, team.Id, owner.Id, ben.Id);
    }

    private async Task<int> CountAsync(Func<PlanFlowDbContext, Task<int>> count)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await count(scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>());
    }

    private static string At(DateTime moment) => moment.ToString("yyyy-MM-ddTHH:mm:ss'Z'");

    /// <summary>Whole seconds in the future, so values come back from JSON and PostgreSQL exactly as sent.</summary>
    private static DateTime In(TimeSpan span)
    {
        var moment = DateTime.UtcNow.Add(span);
        return new DateTime(moment.Ticks - moment.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Event_CreatedWithEveryNewField_IsReadBackEditedAndDeletedWithItsAlertsAndGuests()
    {
        var s = await SceneAsync();
        var start = In(TimeSpan.FromDays(2));

        var created = await s.Owner.PostAsJsonAsync($"api/teams/{s.TeamId}/tasks", new
        {
            title = "Planning", description = "Agenda in the doc", assignedUserId = s.OwnerId, impactScore = 6,
            taskType = (int)TaskType.Event, startUtc = At(start), endUtc = At(start.AddHours(2)), estimatedEffortHours = 1.5,
            location = "Room 4", colorKey = "tomato", isBusy = false,
            reminders = new object[] { new { offsetMinutes = 30 }, new { daysBefore = 1, timeOfDayMinutes = 540 } },
            participantUserIds = new[] { s.BenId }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await Json(created)).GetProperty("id").GetGuid();

        var detail = await s.Owner.GetFromJsonAsync<JsonElement>($"api/tasks/{id}");
        var task = detail.GetProperty("task");
        Assert.Equal("Room 4", task.GetProperty("location").GetString());
        Assert.Equal("tomato", task.GetProperty("colorKey").GetString());
        Assert.False(task.GetProperty("isBusy").GetBoolean());
        Assert.Equal(1.5, task.GetProperty("estimatedEffortHours").GetDouble());
        Assert.Equal(start, task.GetProperty("startUtc").GetDateTime().ToUniversalTime());
        Assert.Equal(2, detail.GetProperty("reminders").GetArrayLength());
        Assert.Equal([s.BenId], task.GetProperty("participantUserIds").EnumerateArray().Select(e => e.GetGuid()));
        Assert.Equal(2, await CountAsync(db => db.ItemReminders.CountAsync(r => r.CalendarItemId == id)));
        Assert.Equal(1, await CountAsync(db => db.ItemParticipants.CountAsync(p => p.CalendarItemId == id)));

        // The edit replaces both lists.
        var edit = await s.Owner.PutAsJsonAsync($"api/tasks/{id}", new
        {
            title = "Planning (moved)", status = (int)TaskStatus.Confirmed, assignedUserId = s.OwnerId, impactScore = 6,
            taskType = (int)TaskType.Event, startUtc = At(start), endUtc = At(start.AddHours(2)), eventTemplate = (int)EventTemplate.Default,
            location = "Room 9", colorKey = "basil", isBusy = true,
            reminders = new object[] { new { offsetMinutes = 10 } }, participantUserIds = Array.Empty<Guid>()
        });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var edited = (await s.Owner.GetFromJsonAsync<JsonElement>($"api/tasks/{id}")).GetProperty("task");
        Assert.Equal("Room 9", edited.GetProperty("location").GetString());
        Assert.Equal("basil", edited.GetProperty("colorKey").GetString());
        Assert.True(edited.GetProperty("isBusy").GetBoolean());
        Assert.Equal(1, await CountAsync(db => db.ItemReminders.CountAsync(r => r.CalendarItemId == id)));
        Assert.Equal(0, await CountAsync(db => db.ItemParticipants.CountAsync(p => p.CalendarItemId == id)));

        // Deleting the item takes its alerts with it.
        Assert.Equal(HttpStatusCode.NoContent, (await s.Owner.DeleteAsync($"api/tasks/{id}")).StatusCode);
        Assert.Equal(0, await CountAsync(db => db.ItemReminders.CountAsync(r => r.CalendarItemId == id)));
    }

    [Fact]
    public async Task Item_WithATextLongerThanItsColumn_IsRefusedWith400NotAnError500()
    {
        var s = await SceneAsync();
        var start = In(TimeSpan.FromDays(2));

        var longPlace = await s.Owner.PostAsJsonAsync($"api/teams/{s.TeamId}/tasks", new
        {
            title = "Offsite", impactScore = 5, taskType = (int)TaskType.Event, startUtc = At(start), endUtc = At(start.AddHours(1)), location = new string('L', 600)
        });
        var longTitle = await s.Owner.PostAsJsonAsync($"api/teams/{s.TeamId}/tasks", new { title = new string('T', 300), impactScore = 5 });

        Assert.Equal(HttpStatusCode.BadRequest, longPlace.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, longTitle.StatusCode);
    }

    [Fact]
    public async Task RepeatingEvent_EditFromHereOnAndDeleteWithAScope_ReachTheRightOccurrencesOnRealPostgres()
    {
        var s = await SceneAsync();
        var start = new DateTime(DateTime.UtcNow.Date.AddDays(1).Ticks, DateTimeKind.Utc).AddHours(9);

        var created = await s.Owner.PostAsJsonAsync($"api/teams/{s.TeamId}/tasks", new
        {
            title = "Standup", impactScore = 5, taskType = (int)TaskType.Event, startUtc = At(start), endUtc = At(start.AddMinutes(30)),
            isRecurring = true, recurrenceFrequency = (int)RecurrenceFrequency.Daily, recurrenceInterval = 1
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        async Task<List<(Guid Id, string Title, DateTime Start)>> OccurrencesAsync(params string[] titles) =>
            (await s.Owner.GetFromJsonAsync<List<JsonElement>>($"api/teams/{s.TeamId}/tasks"))!
                .Where(t => titles.Contains(t.GetProperty("title").GetString()))
                .Select(t => (t.GetProperty("id").GetGuid(), t.GetProperty("title").GetString()!, t.GetProperty("startUtc").GetDateTime().ToUniversalTime()))
                .OrderBy(t => t.Item3).ToList();

        var all = await OccurrencesAsync("Standup");
        Assert.InRange(all.Count, 88, 92); // 90 days ahead, one a day
        var tenth = all[9];

        // "This and following" on the tenth: the nine before keep their name, the rest take the new one.
        var edit = await s.Owner.PutAsJsonAsync($"api/tasks/{tenth.Id}", new
        {
            title = "Standup moved", status = (int)TaskStatus.Confirmed, impactScore = 5, taskType = (int)TaskType.Event,
            startUtc = At(tenth.Start), endUtc = At(tenth.Start.AddMinutes(30)),
            isRecurring = true, recurrenceFrequency = (int)RecurrenceFrequency.Daily, recurrenceInterval = 1, scope = (int)SeriesScope.ThisAndFollowing
        });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        var before = await OccurrencesAsync("Standup");
        var after = await OccurrencesAsync("Standup moved");
        Assert.Equal(9, before.Count);
        Assert.All(before, o => Assert.True(o.Start < tenth.Start));
        Assert.True(after.Count >= 70);
        Assert.All(after, o => Assert.True(o.Start >= tenth.Start));

        // "All" on the new part removes that part and leaves the earlier one.
        Assert.Equal(HttpStatusCode.NoContent, (await s.Owner.DeleteAsync($"api/tasks/{after[0].Id}?scope={(int)SeriesScope.All}")).StatusCode);
        Assert.Empty(await OccurrencesAsync("Standup moved"));
        Assert.Equal(9, (await OccurrencesAsync("Standup")).Count);

        // "This" removes one occurrence only.
        Assert.Equal(HttpStatusCode.NoContent, (await s.Owner.DeleteAsync($"api/tasks/{before[0].Id}?scope={(int)SeriesScope.This}")).StatusCode);
        Assert.Equal(8, (await OccurrencesAsync("Standup")).Count);
    }

    [Fact]
    public async Task ScoreHistory_ListsEveryComputation_NewestFirst_EachWithItsReason()
    {
        var s = await SceneAsync();
        var created = await s.Owner.PostAsJsonAsync($"api/teams/{s.TeamId}/tasks", new { title = "Report", impactScore = 8, deadlineUtc = At(In(TimeSpan.FromDays(10))) });
        var id = (await Json(created)).GetProperty("id").GetGuid();

        // Moving the deadline much closer must raise the score and leave a second entry.
        var edit = await s.Owner.PutAsJsonAsync($"api/tasks/{id}", new
        {
            title = "Report", status = (int)TaskStatus.Backlog, impactScore = 8, deadlineUtc = At(In(TimeSpan.FromDays(1)))
        });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        var history = await s.Owner.GetFromJsonAsync<List<JsonElement>>($"api/tasks/{id}/score-history");

        Assert.Equal(2, history!.Count);
        Assert.Equal((int)ScoreTriggerSource.ManualUpdate, history[0].GetProperty("triggerSource").GetInt32());
        Assert.Equal((int)ScoreTriggerSource.ManualCreate, history[1].GetProperty("triggerSource").GetInt32());
        Assert.True(history[0].GetProperty("finalScore").GetDouble() > history[1].GetProperty("finalScore").GetDouble());
        Assert.All(history, h => Assert.False(string.IsNullOrWhiteSpace(h.GetProperty("reasonText").GetString())));
    }
}
