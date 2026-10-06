using FluentValidation.TestHelper;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Notifications;
using PlanFlow.Application.Notifications.Inbox;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;

namespace PlanFlow.Tests.Application.Notifications;

/// <summary>The in-app side of a notification: which channels an event reaches, and the inbox / push-token handlers.</summary>
public class InAppNotificationTests
{
    private static Func<string, EmailContent> Content(string subject = "New task for you", string preview = "Ann assigned a task to you.") =>
        name => new EmailContent(subject, $"<p>Hi {name}</p>", $"Hi {name}", preview);

    // ---------- policy ----------

    [Theory]
    [InlineData(NotificationEvent.Welcome)]
    [InlineData(NotificationEvent.PasswordReset)]
    [InlineData(NotificationEvent.PasswordChanged)]
    [InlineData(NotificationEvent.EmailVerification)]
    [InlineData(NotificationEvent.NewDeviceLogin)]
    [InlineData(NotificationEvent.ChatDigest)]
    public void AccountMailsAndTheChatDigest_HaveNoInAppForm(NotificationEvent evt)
    {
        var prefs = NotificationPreference.CreateDefault(Guid.NewGuid());
        prefs.ChatDigest = true;

        Assert.False(NotificationPolicy.HasInAppForm(evt));
        Assert.False(NotificationPolicy.IsInboxAllowed(prefs, evt));
        Assert.False(NotificationPolicy.IsPushAllowed(prefs, evt));
    }

    [Fact]
    public void PushNeedsItsOwnMasterSwitch_ButTheInboxDoesNot()
    {
        var prefs = NotificationPreference.CreateDefault(Guid.NewGuid());
        prefs.PushEnabled = false;

        Assert.True(NotificationPolicy.IsInboxAllowed(prefs, NotificationEvent.TaskAssignedToYou));
        Assert.False(NotificationPolicy.IsPushAllowed(prefs, NotificationEvent.TaskAssignedToYou));
    }

    [Fact]
    public void TheEmailMasterSwitch_DoesNotSilenceInboxOrPush()
    {
        var prefs = NotificationPreference.CreateDefault(Guid.NewGuid());
        prefs.EmailEnabled = false;

        Assert.False(NotificationPolicy.IsAllowed(prefs, NotificationEvent.TaskAssignedToYou));
        Assert.True(NotificationPolicy.IsInboxAllowed(prefs, NotificationEvent.TaskAssignedToYou));
        Assert.True(NotificationPolicy.IsPushAllowed(prefs, NotificationEvent.TaskAssignedToYou));
    }

    [Fact]
    public void ACategorySwitchSilencesEveryChannel()
    {
        var prefs = NotificationPreference.CreateDefault(Guid.NewGuid());
        prefs.TaskAssignments = false;

        Assert.False(NotificationPolicy.IsAllowed(prefs, NotificationEvent.TaskAssignedToYou));
        Assert.False(NotificationPolicy.IsInboxAllowed(prefs, NotificationEvent.TaskAssignedToYou));
        Assert.False(NotificationPolicy.IsPushAllowed(prefs, NotificationEvent.TaskAssignedToYou));
    }

    // ---------- NotificationService ----------

    [Fact]
    public async Task Notify_CreatesAnInboxEntryWithTitleBodyAndTarget_AndAsksForAPush()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        var team = Guid.NewGuid();
        var task = Guid.NewGuid();

        await new NotificationService(db).NotifyAsync(
            NotificationEvent.TaskAssignedToYou, new[] { user.Id }, Content(), team, taskId: task);
        await db.SaveChangesAsync();

        var entry = Assert.Single(db.Notifications);
        Assert.Equal(user.Id, entry.UserId);
        Assert.Equal("New task for you", entry.Title);
        Assert.Equal("Ann assigned a task to you.", entry.Body);
        Assert.Equal(team, entry.TeamId);
        Assert.Equal(task, entry.TaskId);
        Assert.Null(entry.ReadAtUtc);
        Assert.Equal(PushDeliveryState.Pending, entry.PushState);
        Assert.Single(db.EmailOutbox); // the mail is still queued alongside
    }

    [Fact]
    public async Task Notify_PushSwitchedOff_KeepsTheInboxEntryButNoPush()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        var prefs = NotificationPreference.CreateDefault(user.Id);
        prefs.PushEnabled = false;
        db.NotificationPreferences.Add(prefs);
        await db.SaveChangesAsync();

        await new NotificationService(db).NotifyAsync(NotificationEvent.TaskChanged, new[] { user.Id }, Content());
        await db.SaveChangesAsync();

        Assert.Equal(PushDeliveryState.NotWanted, Assert.Single(db.Notifications).PushState);
    }

    [Fact]
    public async Task Notify_UnconfirmedAddress_StillGetsTheInboxEntryButNoMail()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        user.EmailVerifiedAtUtc = null;
        await db.SaveChangesAsync();

        await new NotificationService(db).NotifyAsync(NotificationEvent.TaskChanged, new[] { user.Id }, Content());
        await db.SaveChangesAsync();

        Assert.Single(db.Notifications);
        Assert.Empty(db.EmailOutbox);
    }

    [Fact]
    public async Task Notify_EmailSwitchedOff_StillFillsTheInbox()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        var prefs = NotificationPreference.CreateDefault(user.Id);
        prefs.EmailEnabled = false;
        db.NotificationPreferences.Add(prefs);
        await db.SaveChangesAsync();

        await new NotificationService(db).NotifyAsync(NotificationEvent.TaskChanged, new[] { user.Id }, Content());
        await db.SaveChangesAsync();

        Assert.Single(db.Notifications);
        Assert.Empty(db.EmailOutbox);
    }

    [Fact]
    public async Task Notify_MutedTeam_SilencesTheInboxToo()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (user, TeamRole.Member));
        db.TeamMembers.Single().MuteNotifications = true;
        await db.SaveChangesAsync();

        await new NotificationService(db).NotifyAsync(NotificationEvent.TaskChanged, new[] { user.Id }, Content(), team.Id);
        await db.SaveChangesAsync();

        Assert.Empty(db.Notifications);
        Assert.Empty(db.EmailOutbox);
    }

    [Fact]
    public async Task Notify_NeverNotifiesTheActor_InTheInboxEither()
    {
        var db = TestDb.Create();
        var actor = await TestDb.AddUserAsync(db);

        await new NotificationService(db).NotifyAsync(NotificationEvent.TaskChanged, new[] { actor.Id }, Content(), actorUserId: actor.Id);
        await db.SaveChangesAsync();

        Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task Notify_TheChatDigest_IsMailOnly()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        db.NotificationPreferences.Add(new NotificationPreference { UserId = user.Id, ChatDigest = true });
        await db.SaveChangesAsync();

        await new NotificationService(db).NotifyAsync(NotificationEvent.ChatDigest, new[] { user.Id }, Content());
        await db.SaveChangesAsync();

        Assert.Empty(db.Notifications);
        Assert.Single(db.EmailOutbox);
    }

    [Fact]
    public async Task QueueDirect_AccountMail_NeverTouchesTheInbox()
    {
        var db = TestDb.Create();

        new NotificationService(db).QueueDirect(NotificationEvent.Welcome, "new@example.com", "New", Content());
        await db.SaveChangesAsync();

        Assert.Single(db.EmailOutbox);
        Assert.Empty(db.Notifications);
    }

    // ---------- inbox handlers ----------

    private static Notification Entry(Guid userId, DateTime createdUtc, bool read = false) => new()
    {
        UserId = userId, Event = NotificationEvent.TaskChanged, Title = "T", Body = "B", CreatedAtUtc = createdUtc,
        ReadAtUtc = read ? createdUtc.AddMinutes(1) : null
    };

    [Fact]
    public async Task GetNotifications_ReturnsOnlyMyEntries_NewestFirst_AndPagesByTime()
    {
        var db = TestDb.Create();
        var me = await TestDb.AddUserAsync(db);
        var other = await TestDb.AddUserAsync(db);
        var now = DateTime.UtcNow;
        db.Notifications.AddRange(
            Entry(me.Id, now.AddMinutes(-3)), Entry(me.Id, now.AddMinutes(-2)), Entry(me.Id, now.AddMinutes(-1)), Entry(other.Id, now));
        await db.SaveChangesAsync();
        var handler = new GetNotificationsQueryHandler(db);

        var firstPage = await handler.Handle(new GetNotificationsQuery(me.Id, null, 2), CancellationToken.None);
        Assert.Equal(2, firstPage.Count);
        Assert.True(firstPage[0].CreatedAtUtc > firstPage[1].CreatedAtUtc);

        var secondPage = await handler.Handle(new GetNotificationsQuery(me.Id, firstPage[^1].CreatedAtUtc, 2), CancellationToken.None);
        var last = Assert.Single(secondPage);
        Assert.True(last.CreatedAtUtc < firstPage[^1].CreatedAtUtc);
    }

    [Fact]
    public void GetNotificationsValidator_RejectsAbsurdPageSizes()
    {
        var validator = new GetNotificationsQueryValidator();

        validator.TestValidate(new GetNotificationsQuery(Guid.NewGuid(), null, 0)).ShouldHaveValidationErrorFor(x => x.Limit);
        validator.TestValidate(new GetNotificationsQuery(Guid.NewGuid(), null, 101)).ShouldHaveValidationErrorFor(x => x.Limit);
        validator.TestValidate(new GetNotificationsQuery(Guid.NewGuid(), null, 100)).ShouldNotHaveValidationErrorFor(x => x.Limit);
    }

    [Fact]
    public async Task UnreadCount_CountsOnlyMyUnreadEntries()
    {
        var db = TestDb.Create();
        var me = await TestDb.AddUserAsync(db);
        var other = await TestDb.AddUserAsync(db);
        var now = DateTime.UtcNow;
        db.Notifications.AddRange(Entry(me.Id, now), Entry(me.Id, now), Entry(me.Id, now, read: true), Entry(other.Id, now));
        await db.SaveChangesAsync();

        var count = await new GetUnreadNotificationCountQueryHandler(db).Handle(new GetUnreadNotificationCountQuery(me.Id), CancellationToken.None);

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task MarkRead_SetsTheTimeOnce_AndNeverTouchesSomebodyElsesEntry()
    {
        var db = TestDb.Create();
        var me = await TestDb.AddUserAsync(db);
        var other = await TestDb.AddUserAsync(db);
        var mine = Entry(me.Id, DateTime.UtcNow);
        var theirs = Entry(other.Id, DateTime.UtcNow);
        db.Notifications.AddRange(mine, theirs);
        await db.SaveChangesAsync();
        var handler = new MarkNotificationReadCommandHandler(db);

        await handler.Handle(new MarkNotificationReadCommand(me.Id, mine.Id), CancellationToken.None);
        var firstRead = db.Notifications.Single(n => n.Id == mine.Id).ReadAtUtc;
        await handler.Handle(new MarkNotificationReadCommand(me.Id, mine.Id), CancellationToken.None);

        Assert.NotNull(firstRead);
        Assert.Equal(firstRead, db.Notifications.Single(n => n.Id == mine.Id).ReadAtUtc);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new MarkNotificationReadCommand(me.Id, theirs.Id), CancellationToken.None));
        Assert.Null(db.Notifications.Single(n => n.Id == theirs.Id).ReadAtUtc);
    }

    [Fact]
    public async Task MarkAllRead_OnlyAffectsTheCaller()
    {
        var db = TestDb.Create();
        var me = await TestDb.AddUserAsync(db);
        var other = await TestDb.AddUserAsync(db);
        db.Notifications.AddRange(Entry(me.Id, DateTime.UtcNow), Entry(me.Id, DateTime.UtcNow), Entry(other.Id, DateTime.UtcNow));
        await db.SaveChangesAsync();

        await new MarkAllNotificationsReadCommandHandler(db).Handle(new MarkAllNotificationsReadCommand(me.Id), CancellationToken.None);

        Assert.All(db.Notifications.Where(n => n.UserId == me.Id), n => Assert.NotNull(n.ReadAtUtc));
        Assert.Null(db.Notifications.Single(n => n.UserId == other.Id).ReadAtUtc);
    }

    // ---------- push tokens ----------

    private const string TokenA = "ExponentPushToken[aaaaaaaaaaaaaaaaaaaaaa]";

    [Fact]
    public async Task RegisterToken_IsIdempotent()
    {
        var db = TestDb.Create();
        var me = await TestDb.AddUserAsync(db);
        var handler = new RegisterPushTokenCommandHandler(db);

        await handler.Handle(new RegisterPushTokenCommand(me.Id, TokenA, "ios"), CancellationToken.None);
        await handler.Handle(new RegisterPushTokenCommand(me.Id, TokenA, "ios"), CancellationToken.None);

        Assert.Equal(me.Id, Assert.Single(db.PushTokens).UserId);
    }

    [Fact]
    public async Task RegisterToken_OnAPhoneThatChangedHands_MovesTheTokenToTheNewUser()
    {
        var db = TestDb.Create();
        var ann = await TestDb.AddUserAsync(db);
        var bob = await TestDb.AddUserAsync(db);
        var handler = new RegisterPushTokenCommandHandler(db);

        await handler.Handle(new RegisterPushTokenCommand(ann.Id, TokenA, "ios"), CancellationToken.None);
        await handler.Handle(new RegisterPushTokenCommand(bob.Id, TokenA, "ios"), CancellationToken.None);

        Assert.Equal(bob.Id, Assert.Single(db.PushTokens).UserId); // Ann no longer gets pushes meant for Bob's phone
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("ExponentPushToken[]")]
    [InlineData("ExponentPushToken[has space]")]
    public void RegisterToken_RejectsAnythingThatIsNotAnExpoToken(string token)
    {
        new RegisterPushTokenCommandValidator()
            .TestValidate(new RegisterPushTokenCommand(Guid.NewGuid(), token, "ios"))
            .ShouldHaveValidationErrorFor(x => x.Token);
    }

    [Fact]
    public void RegisterToken_AcceptsARealLookingToken() =>
        new RegisterPushTokenCommandValidator()
            .TestValidate(new RegisterPushTokenCommand(Guid.NewGuid(), TokenA, "android"))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public async Task UnregisterToken_RemovesOnlyTheCallersOwnToken()
    {
        var db = TestDb.Create();
        var ann = await TestDb.AddUserAsync(db);
        var bob = await TestDb.AddUserAsync(db);
        db.PushTokens.Add(new PushToken { UserId = ann.Id, Token = TokenA, Platform = "ios" });
        await db.SaveChangesAsync();
        var handler = new UnregisterPushTokenCommandHandler(db);

        await handler.Handle(new UnregisterPushTokenCommand(bob.Id, TokenA), CancellationToken.None);
        Assert.Single(db.PushTokens);

        await handler.Handle(new UnregisterPushTokenCommand(ann.Id, TokenA), CancellationToken.None);
        Assert.Empty(db.PushTokens);
    }
}
