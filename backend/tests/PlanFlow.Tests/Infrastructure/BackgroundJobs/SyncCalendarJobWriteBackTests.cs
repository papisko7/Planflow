using Microsoft.EntityFrameworkCore;
using Moq;
using PlanFlow.Application.Calendar.Common;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.BackgroundJobs;
using PlanFlow.Infrastructure.Persistence;
using Quartz;
using Xunit;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Infrastructure.BackgroundJobs;

/// <summary>
/// The calendar sync in both directions: an edit made in the app reaches Google, and when both sides changed the same event the later
/// change wins (last write wins, decided from Google's update time and the time of the edit in the app).
/// </summary>
public class SyncCalendarJobWriteBackTests
{
    private const string ReadOnlyScopes = "openid email https://www.googleapis.com/auth/calendar.readonly https://www.googleapis.com/auth/tasks.readonly";
    private const string WriteScopes = "openid email " + CalendarIntegration.CalendarWriteScope + " " + CalendarIntegration.TasksWriteScope;

    /// <summary>Google's calendar: its own copy of each event (a patch changes it like the real service) and a record of what was asked of it.</summary>
    private sealed class FakeCalendar : IGoogleCalendarClient
    {
        public Dictionary<string, GoogleCalendarEvent> Server { get; } = [];
        public HashSet<string> Listed { get; } = [];
        public List<(string Id, GoogleEventPatch Patch)> Patches { get; } = [];
        public List<string> Gets { get; } = [];
        public Dictionary<string, Exception> PatchFailures { get; } = [];

        public Task<IReadOnlyList<GoogleCalendarEvent>> ListEventsAsync(string accessToken, DateTime? updatedSinceUtc, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GoogleCalendarEvent>>(Listed.Where(Server.ContainsKey).Select(id => Server[id]).ToList());

        public Task<GoogleCalendarEvent?> GetEventAsync(string accessToken, string eventId, CancellationToken cancellationToken)
        {
            Gets.Add(eventId);
            return Task.FromResult(Server.GetValueOrDefault(eventId));
        }

        public Task<GoogleCalendarEvent> PatchEventAsync(string accessToken, string eventId, GoogleEventPatch patch, CancellationToken cancellationToken)
        {
            Patches.Add((eventId, patch));
            if (PatchFailures.TryGetValue(eventId, out var failure))
            {
                throw failure;
            }

            var e = Server[eventId];
            Server[eventId] = e with
            {
                Title = patch.Title ?? e.Title,
                Description = patch.Description is null ? e.Description : patch.Description.Length == 0 ? null : patch.Description,
                StartUtc = patch.Time?.StartUtc ?? e.StartUtc,
                EndUtc = patch.Time?.EndUtc ?? e.EndUtc,
                IsAllDay = patch.Time?.IsAllDay ?? e.IsAllDay,
                Location = patch.Location is null ? e.Location : patch.Location.Length == 0 ? null : patch.Location,
                ColorId = patch.ColorId is null ? e.ColorId : patch.ColorId.Length == 0 ? null : patch.ColorId,
                IsFree = patch.IsFree ?? e.IsFree,
                ReminderMinutes = patch.ReminderMinutes ?? e.ReminderMinutes,
                UpdatedUtc = DateTime.UtcNow
            };
            return Task.FromResult(Server[eventId]);
        }
    }

    private sealed class Harness
    {
        public Harness(string scopes = WriteScopes)
        {
            Context = new PlanFlowDbContext(new DbContextOptionsBuilder<PlanFlowDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var user = new User { Email = "student@example.com", DisplayName = "Student", PasswordHash = "hash", EmailVerifiedAtUtc = DateTime.UtcNow };
            var team = new Team { Name = "Personal", IsPersonal = true };
            Context.Users.Add(user);
            Context.Teams.Add(team);
            Context.TeamMembers.Add(new TeamMember { UserId = user.Id, TeamId = team.Id, Role = TeamRole.Owner });
            Integration = new CalendarIntegration
            {
                UserId = user.Id, Provider = CalendarProvider.Google, ExternalAccountId = "google-sub", ExternalAccountEmail = "student@example.com",
                EncryptedAccessToken = "access-token", EncryptedRefreshToken = "refresh-token", AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1),
                GrantedScopes = scopes, IsActive = true
            };
            Context.CalendarIntegrations.Add(Integration);
            Context.SaveChanges();
        }

        public PlanFlowDbContext Context { get; }
        public CalendarIntegration Integration { get; }
        public FakeCalendar Google { get; } = new();
        public Mock<IGoogleOAuthClient> OAuth { get; } = new();

        public async Task SyncAsync()
        {
            var encryption = new Mock<ITokenEncryptionService>();
            encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns<string>(s => s);
            encryption.Setup(e => e.Decrypt(It.IsAny<string>())).Returns<string>(s => s);
            var cache = new Mock<ICacheService>();
            cache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var job = new SyncCalendarJob(
                Context, new GoogleAccessTokenProvider(OAuth.Object, encryption.Object, Context), Google, cache.Object,
                new PlanFlow.Application.Notifications.NotificationService(Context), Mock.Of<Microsoft.Extensions.Logging.ILogger<SyncCalendarJob>>());

            var jobContext = new Mock<IJobExecutionContext>();
            jobContext.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
            jobContext.SetupGet(c => c.MergedJobDataMap).Returns(new JobDataMap());
            await job.Execute(jobContext.Object);
        }

        /// <summary>Google holds the events and the first sync imports them.</summary>
        public async Task ImportAsync(params GoogleCalendarEvent[] events)
        {
            foreach (var e in events)
            {
                Google.Server[e.Id] = e;
                Google.Listed.Add(e.Id);
            }
            await SyncAsync();
            Google.Listed.Clear();
        }

        public CalendarItem Item(string eventId) => Context.CalendarItems.Include(i => i.Reminders).Single(i => i.ExternalCalendarEventId == eventId);

        /// <summary>An edit made in the app at the given moment, noted the way the edit command notes it.</summary>
        public CalendarItem EditInApp(string eventId, DateTime at, Action<CalendarItem> edit)
        {
            var item = Item(eventId);
            var before = GoogleMirror.Capture(item);
            edit(item);
            GoogleMirror.Track(item, before, GoogleMirror.Capture(item), at);
            Context.SaveChanges();
            return item;
        }

        /// <summary>The alerts changed in the app: replaced through the context, as the edit command does, and noted from the new list.</summary>
        public void SetRemindersInApp(string eventId, DateTime at, params ItemReminder[] wanted)
        {
            var item = Item(eventId);
            var before = GoogleMirror.Capture(item);
            Context.ItemReminders.RemoveRange(item.Reminders.ToList());
            foreach (var reminder in wanted)
            {
                reminder.CalendarItemId = item.Id;
                Context.ItemReminders.Add(reminder);
            }
            GoogleMirror.Track(item, before, GoogleMirror.Capture(item, wanted.Select(w => w.Key)), at);
            Context.SaveChanges();
        }

        /// <summary>An edit made in Google at the given moment.</summary>
        public void EditInGoogle(string eventId, DateTime at, Func<GoogleCalendarEvent, GoogleCalendarEvent> edit)
        {
            Google.Server[eventId] = edit(Google.Server[eventId]) with { UpdatedUtc = at };
            Google.Listed.Add(eventId);
        }
    }

    private static readonly DateTime Start = DateTime.UtcNow.Date.AddDays(2).AddHours(10);

    private static GoogleCalendarEvent Standup(string id = "e1") => new(
        id, "Standup", Start, Start.AddHours(1), IsCancelled: false, Location: "Room 1", ReminderMinutes: [], UpdatedUtc: DateTime.UtcNow.AddDays(-1));

    // ---- an edit in the app reaches Google ----

    [Fact]
    public async Task EditInTheApp_ThatGoogleDidNotListEither_IsReadAndThenSent()
    {
        var h = new Harness();
        await h.ImportAsync(Standup());
        h.EditInApp("e1", DateTime.UtcNow.AddMinutes(-5), i => { i.Title = "Daily"; i.Location = "Room 7"; });

        await h.SyncAsync();

        Assert.Equal(["e1"], h.Google.Gets);                       // not listed (unchanged in Google): read first
        var (id, patch) = Assert.Single(h.Google.Patches);
        Assert.Equal("e1", id);
        Assert.Equal("Daily", patch.Title);
        Assert.Equal("Room 7", patch.Location);
        Assert.Null(patch.Time);
        Assert.Equal("Daily", h.Google.Server["e1"].Title);
        Assert.Equal("Room 7", h.Google.Server["e1"].Location);

        var item = h.Item("e1");
        Assert.Equal(MirroredFields.None, item.LocalEditedFields);  // nothing waits any more
        Assert.Null(item.LocalEditedAtUtc);
        Assert.Equal("Daily", item.Title);
    }

    [Fact]
    public async Task EditInTheApp_ThatGoogleListedUnchanged_IsSentWithoutReadingTheEvent_EvenIfGoogleTouchedItLater()
    {
        var h = new Harness();
        await h.ImportAsync(Standup());
        h.EditInApp("e1", DateTime.UtcNow.AddHours(-2), i => i.Title = "Daily");
        h.EditInGoogle("e1", DateTime.UtcNow.AddMinutes(-30), e => e);   // touched later (say a guest answered), same content

        await h.SyncAsync();

        Assert.Empty(h.Google.Gets);
        Assert.Equal("Daily", Assert.Single(h.Google.Patches).Patch.Title);   // nothing really changed in Google: no conflict, the edit goes out
        Assert.Equal("Daily", h.Item("e1").Title);
    }

    [Fact]
    public async Task AfterTheEditWasSent_TheNextSyncSeesNoChange_AndSendsNothingAgain()
    {
        var h = new Harness();
        await h.ImportAsync(Standup());
        h.EditInApp("e1", DateTime.UtcNow.AddMinutes(-5), i => { i.Title = "Daily"; i.ColorKey = "tomato"; i.IsBusy = false; });
        await h.SyncAsync();
        var fingerprint = h.Item("e1").ExternalFieldsHash;

        h.Google.Listed.Add("e1");           // Google reports the event again: our own change made it "recently updated"
        await h.SyncAsync();

        Assert.Single(h.Google.Patches);
        Assert.Equal(fingerprint, h.Item("e1").ExternalFieldsHash);
        Assert.Equal("Daily", h.Item("e1").Title);
        Assert.Equal("tomato", h.Item("e1").ColorKey);
        Assert.False(h.Item("e1").IsBusy);
    }

    [Fact]
    public async Task ItemNobodyEditedInTheApp_CostsGoogleNoRequestsBeyondTheList()
    {
        var h = new Harness();
        await h.ImportAsync(Standup(), Standup("e2"));
        h.EditInGoogle("e1", DateTime.UtcNow, e => e with { Title = "Standup (new)" });

        await h.SyncAsync();

        Assert.Empty(h.Google.Gets);
        Assert.Empty(h.Google.Patches);
        Assert.Equal("Standup (new)", h.Item("e1").Title);
    }

    [Fact]
    public async Task RenamingAnEvent_KeepsTheImpactAndEffortWrittenInItsTitleInGoogle()
    {
        var h = new Harness();
        await h.ImportAsync(Standup() with { Title = "Standup [planflow] impact=6 effort=1h" });
        Assert.Equal("Standup", h.Item("e1").Title);
        Assert.Equal(6, h.Item("e1").ImpactScore);
        h.EditInApp("e1", DateTime.UtcNow.AddMinutes(-5), i => i.Title = "Daily");

        await h.SyncAsync();

        Assert.Equal("Daily [planflow] impact=6 effort=1h", h.Google.Server["e1"].Title);
        h.Google.Listed.Add("e1");
        await h.SyncAsync();                                   // reading it back changes nothing
        Assert.Equal("Daily", h.Item("e1").Title);
        Assert.Equal(6, h.Item("e1").ImpactScore);
        Assert.Equal(1, h.Item("e1").EstimatedEffortHours);
    }

    [Fact]
    public async Task ReminderEditedInTheApp_GoesToGoogleAsPopupMinutes()
    {
        var h = new Harness();
        await h.ImportAsync(Standup() with { ReminderMinutes = [30] });
        h.SetRemindersInApp("e1", DateTime.UtcNow.AddMinutes(-5), ItemReminder.Offset(10), ItemReminder.Offset(60));

        await h.SyncAsync();

        Assert.Equal([10, 60], Assert.Single(h.Google.Patches).Patch.ReminderMinutes);
        Assert.Equal([10, 60], h.Google.Server["e1"].ReminderMinutes);
    }

    // ---- both sides changed: the later change wins ----

    [Fact]
    public async Task BothChangedTheSameField_TheLaterEditInTheAppWins_AndIsSent()
    {
        var h = new Harness();
        await h.ImportAsync(Standup());
        h.EditInGoogle("e1", DateTime.UtcNow.AddHours(-3), e => e with { Title = "Standup (Google)" });
        h.EditInApp("e1", DateTime.UtcNow.AddHours(-1), i => i.Title = "Daily");

        await h.SyncAsync();

        Assert.Equal("Daily", h.Item("e1").Title);
        Assert.Equal("Daily", h.Google.Server["e1"].Title);
        Assert.Equal(MirroredFields.None, h.Item("e1").LocalEditedFields);
    }

    [Fact]
    public async Task BothChangedTheSameField_TheLaterChangeInGoogleWins_AndNothingIsSent()
    {
        var h = new Harness();
        await h.ImportAsync(Standup());
        h.EditInApp("e1", DateTime.UtcNow.AddHours(-3), i => i.Title = "Daily");
        h.EditInGoogle("e1", DateTime.UtcNow.AddHours(-1), e => e with { Title = "Standup (Google)" });

        await h.SyncAsync();

        Assert.Equal("Standup (Google)", h.Item("e1").Title);
        Assert.Empty(h.Google.Patches);
        var item = h.Item("e1");
        Assert.Equal(MirroredFields.None, item.LocalEditedFields);
        Assert.Null(item.LocalEditedAtUtc);
    }

    [Fact]
    public async Task EditInTheAppIsLater_ButGoogleAlsoChangedAnotherField_BothSurvive()
    {
        var h = new Harness();
        await h.ImportAsync(Standup());
        h.EditInGoogle("e1", DateTime.UtcNow.AddHours(-3), e => e with { Location = "Room 2" });
        h.EditInApp("e1", DateTime.UtcNow.AddHours(-1), i => i.Title = "Daily");

        await h.SyncAsync();

        var item = h.Item("e1");
        Assert.Equal("Daily", item.Title);                 // the edit of the app (the later one) stays and is sent
        Assert.Equal("Room 2", item.Location);             // what only Google changed is taken over
        Assert.Equal("Daily", h.Google.Server["e1"].Title);
        Assert.Equal("Room 2", h.Google.Server["e1"].Location);
        Assert.Equal("Daily", Assert.Single(h.Google.Patches).Patch.Title);
        Assert.Null(h.Google.Patches[0].Patch.Location);
    }

    [Fact]
    public async Task GoogleWithoutAnUpdateTime_CannotProveTheEditInTheAppIsNewer_SoGoogleWins()
    {
        var h = new Harness();
        await h.ImportAsync(Standup());
        h.EditInApp("e1", DateTime.UtcNow.AddMinutes(-5), i => i.Title = "Daily");
        h.EditInGoogle("e1", DateTime.UtcNow, e => e with { Title = "Standup (Google)", UpdatedUtc = null });

        await h.SyncAsync();

        Assert.Equal("Standup (Google)", h.Item("e1").Title);
        Assert.Empty(h.Google.Patches);
    }

    [Fact]
    public async Task EventDeletedInGoogle_WinsOverAnEditWaitingInTheApp()
    {
        var h = new Harness();
        await h.ImportAsync(Standup());
        h.EditInApp("e1", DateTime.UtcNow.AddMinutes(-5), i => i.Title = "Daily");
        h.EditInGoogle("e1", DateTime.UtcNow.AddMinutes(-60), e => e with { IsCancelled = true, Status = "cancelled" });

        await h.SyncAsync();

        var item = h.Item("e1");
        Assert.Equal(TaskStatus.Cancelled, item.Status);
        Assert.Equal(MirroredFields.None, item.LocalEditedFields);
        Assert.Empty(h.Google.Patches);
    }

    [Fact]
    public async Task EventAlreadyGoneWhenTheEditIsSent_IsCancelledHere()
    {
        var h = new Harness();
        await h.ImportAsync(Standup());
        h.EditInApp("e1", DateTime.UtcNow.AddMinutes(-5), i => i.Title = "Daily");
        h.Google.Server.Remove("e1");

        await h.SyncAsync();

        Assert.Equal(TaskStatus.Cancelled, h.Item("e1").Status);
        Assert.Equal(MirroredFields.None, h.Item("e1").LocalEditedFields);
        Assert.Empty(h.Google.Patches);
    }

    // ---- when Google does not take the edit ----

    [Fact]
    public async Task WithoutThePermissionToChangeTheCalendar_TheEditWaits_AndGoesOutOnceTheUserReconnects()
    {
        var h = new Harness(ReadOnlyScopes);
        await h.ImportAsync(Standup());
        h.EditInApp("e1", DateTime.UtcNow.AddMinutes(-5), i => i.Title = "Daily");

        await h.SyncAsync();

        Assert.Empty(h.Google.Gets);
        Assert.Empty(h.Google.Patches);
        Assert.Equal(MirroredFields.Title, h.Item("e1").LocalEditedFields);   // still waiting
        Assert.Equal("Daily", h.Item("e1").Title);                            // and still the user's version here

        h.Integration.GrantedScopes = WriteScopes;                            // reconnected
        await h.Context.SaveChangesAsync();
        await h.SyncAsync();

        Assert.Equal("Daily", h.Google.Server["e1"].Title);
        Assert.Equal(MirroredFields.None, h.Item("e1").LocalEditedFields);
    }

    [Fact]
    public async Task GoogleRefusesOneEvent_ItsEditStaysInTheAppOnly_AndTheOtherEventsAreStillSent()
    {
        var h = new Harness();
        await h.ImportAsync(Standup(), Standup("e2"));
        h.EditInApp("e1", DateTime.UtcNow.AddMinutes(-5), i => i.Title = "Daily");
        h.EditInApp("e2", DateTime.UtcNow.AddMinutes(-5), i => i.Title = "Weekly");
        h.Google.PatchFailures["e1"] = new GoogleWriteRejectedException(GoogleWriteFailure.Rejected, "organised by somebody else");

        await h.SyncAsync();

        Assert.Equal("Weekly", h.Google.Server["e2"].Title);
        Assert.Equal("Standup", h.Google.Server["e1"].Title);
        Assert.Equal("Daily", h.Item("e1").Title);                              // the user's version stays here
        Assert.Equal(MirroredFields.None, h.Item("e1").LocalEditedFields);      // and is not offered again and again
        Assert.Equal(MirroredFields.None, h.Item("e2").LocalEditedFields);
    }

    [Fact]
    public async Task GoogleSaysTheEventIsGoneWhileSending_ItIsCancelledHere()
    {
        var h = new Harness();
        await h.ImportAsync(Standup());
        h.EditInApp("e1", DateTime.UtcNow.AddMinutes(-5), i => i.Title = "Daily");
        h.Google.PatchFailures["e1"] = new GoogleWriteRejectedException(GoogleWriteFailure.Gone, "gone");

        await h.SyncAsync();

        Assert.Equal(TaskStatus.Cancelled, h.Item("e1").Status);
        Assert.Equal(MirroredFields.None, h.Item("e1").LocalEditedFields);
    }

    [Fact]
    public async Task PermissionRefusedByGoogle_StopsSending_AndEveryEditKeepsWaiting()
    {
        var h = new Harness();
        await h.ImportAsync(Standup(), Standup("e2"));
        h.EditInApp("e1", DateTime.UtcNow.AddMinutes(-5), i => i.Title = "Daily");
        h.EditInApp("e2", DateTime.UtcNow.AddMinutes(-5), i => i.Title = "Weekly");
        h.Google.PatchFailures["e1"] = new GoogleWriteRejectedException(GoogleWriteFailure.MissingPermission, "scopes");
        h.Google.PatchFailures["e2"] = new GoogleWriteRejectedException(GoogleWriteFailure.MissingPermission, "scopes");

        await h.SyncAsync();

        Assert.Single(h.Google.Patches);                                         // the second one is not even tried
        Assert.Equal(MirroredFields.Title, h.Item("e1").LocalEditedFields);
        Assert.Equal(MirroredFields.Title, h.Item("e2").LocalEditedFields);
    }

    [Fact]
    public async Task TemporaryFailure_LeavesTheEditWaiting_AndTheNextSyncSendsIt()
    {
        var h = new Harness();
        await h.ImportAsync(Standup());
        h.EditInApp("e1", DateTime.UtcNow.AddMinutes(-5), i => i.Title = "Daily");
        h.Google.PatchFailures["e1"] = new HttpRequestException("Google is down");

        await h.SyncAsync();
        Assert.Equal(MirroredFields.Title, h.Item("e1").LocalEditedFields);
        Assert.Equal("Standup", h.Google.Server["e1"].Title);

        h.Google.PatchFailures.Clear();
        await h.SyncAsync();

        Assert.Equal("Daily", h.Google.Server["e1"].Title);
        Assert.Equal(MirroredFields.None, h.Item("e1").LocalEditedFields);
    }

    [Fact]
    public async Task GoogleRejectingTheTokenWhileSending_DeactivatesTheConnection_LikeWhenReading()
    {
        var h = new Harness();
        h.OAuth.Setup(o => o.RefreshAccessTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleTokenResult("fresh", RefreshToken: null, ExpiresInSeconds: 3600, Scope: WriteScopes));
        await h.ImportAsync(Standup());
        h.EditInApp("e1", DateTime.UtcNow.AddMinutes(-5), i => i.Title = "Daily");
        h.Google.PatchFailures["e1"] = new UnauthorizedException("revoked");

        await h.SyncAsync();

        Assert.False(h.Integration.IsActive);
        Assert.Equal(MirroredFields.Title, h.Item("e1").LocalEditedFields);      // kept for when the user connects again
    }

    // ---- what is not sent ----

    [Fact]
    public async Task ImpactAndEffortEditedInTheApp_AreNotSent_BecauseGoogleHasNoFieldForThem()
    {
        var h = new Harness();
        await h.ImportAsync(Standup());
        h.EditInApp("e1", DateTime.UtcNow.AddMinutes(-5), i => { i.ImpactScore = 9; i.EstimatedEffortHours = 2; });

        await h.SyncAsync();

        Assert.Empty(h.Google.Patches);
        Assert.Empty(h.Google.Gets);
        Assert.Equal(9, h.Item("e1").ImpactScore);
    }

    [Fact]
    public async Task BirthdayEditedInTheApp_IsNeverSent()
    {
        var h = new Harness();
        var birthday = new GoogleCalendarEvent(
            "b1", "Ola's birthday", Start.Date, Start.Date.AddDays(1), IsCancelled: false, IsAllDay: true, EventType: "birthday", ReminderMinutes: [],
            UpdatedUtc: DateTime.UtcNow.AddDays(-1));
        await h.ImportAsync(birthday);
        h.EditInApp("b1", DateTime.UtcNow.AddMinutes(-5), i => i.Title = "Ola's party");

        await h.SyncAsync();

        Assert.Empty(h.Google.Patches);
        Assert.Equal(MirroredFields.None, h.Item("b1").LocalEditedFields);
    }
}
