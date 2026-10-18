using FluentValidation.TestHelper;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Notifications.Inbox;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Tests.Application.Notifications;

/// <summary>Step 7.5.8: deleting inbox entries (one, ticked ones, everything) and marking several as read.</summary>
public class InboxCleanupTests
{
    private static Notification Entry(Guid userId, PushDeliveryState push = PushDeliveryState.Sent, bool read = false) => new()
    {
        UserId = userId, Event = NotificationEvent.TaskChanged, Title = "T", Body = "B", PushState = push,
        ReadAtUtc = read ? DateTime.UtcNow : null
    };

    // ---------- delete one ----------

    [Fact]
    public async Task DeleteOne_RemovesTheEntry_EvenWhenItsPushIsStillQueued()
    {
        var db = TestDb.Create();
        var me = await TestDb.AddUserAsync(db);
        var queued = Entry(me.Id, PushDeliveryState.Pending);
        db.Notifications.Add(queued);
        await db.SaveChangesAsync();

        await new DeleteNotificationCommandHandler(db).Handle(new DeleteNotificationCommand(me.Id, queued.Id), CancellationToken.None);

        Assert.Empty(db.Notifications); // the user picked it by hand, so the queued push goes with it
    }

    [Fact]
    public async Task DeleteOne_SomebodyElsesEntry_LooksLikeAMissingOne_AndSurvives()
    {
        var db = TestDb.Create();
        var me = await TestDb.AddUserAsync(db);
        var other = await TestDb.AddUserAsync(db);
        var theirs = Entry(other.Id);
        db.Notifications.Add(theirs);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new DeleteNotificationCommandHandler(db).Handle(new DeleteNotificationCommand(me.Id, theirs.Id), CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            new DeleteNotificationCommandHandler(db).Handle(new DeleteNotificationCommand(me.Id, Guid.NewGuid()), CancellationToken.None));

        Assert.Single(db.Notifications);
    }

    // ---------- delete ticked ----------

    [Fact]
    public async Task DeleteSelected_RemovesOnlyTheTickedEntriesOfTheCaller()
    {
        var db = TestDb.Create();
        var me = await TestDb.AddUserAsync(db);
        var other = await TestDb.AddUserAsync(db);
        var ticked1 = Entry(me.Id);
        var ticked2 = Entry(me.Id, read: true);
        var untouched = Entry(me.Id);
        var theirs = Entry(other.Id);
        db.Notifications.AddRange(ticked1, ticked2, untouched, theirs);
        await db.SaveChangesAsync();

        await new DeleteSelectedNotificationsCommandHandler(db).Handle(
            new DeleteSelectedNotificationsCommand(me.Id, new[] { ticked1.Id, ticked2.Id, theirs.Id, Guid.NewGuid() }), CancellationToken.None);

        var left = db.Notifications.Select(n => n.Id).ToHashSet();
        Assert.Equal(new[] { untouched.Id, theirs.Id }.ToHashSet(), left); // somebody else's id in the list is silently ignored
    }

    [Fact]
    public void Selection_NeedsIdsButNotAbsurdlyMany()
    {
        var delete = new DeleteSelectedNotificationsCommandValidator();
        var read = new MarkSelectedNotificationsReadCommandValidator();
        var many = Enumerable.Range(0, NotificationBatch.MaxIds + 1).Select(_ => Guid.NewGuid()).ToList();
        var allowed = many.Take(NotificationBatch.MaxIds).ToList();

        delete.TestValidate(new DeleteSelectedNotificationsCommand(Guid.NewGuid(), new List<Guid>())).ShouldHaveValidationErrorFor(x => x.NotificationIds);
        delete.TestValidate(new DeleteSelectedNotificationsCommand(Guid.NewGuid(), many)).ShouldHaveValidationErrorFor(x => x.NotificationIds);
        delete.TestValidate(new DeleteSelectedNotificationsCommand(Guid.NewGuid(), allowed)).ShouldNotHaveValidationErrorFor(x => x.NotificationIds);
        read.TestValidate(new MarkSelectedNotificationsReadCommand(Guid.NewGuid(), new List<Guid>())).ShouldHaveValidationErrorFor(x => x.NotificationIds);
        read.TestValidate(new MarkSelectedNotificationsReadCommand(Guid.NewGuid(), many)).ShouldHaveValidationErrorFor(x => x.NotificationIds);
    }

    // ---------- clear all ----------

    [Fact]
    public async Task Clear_RemovesEverythingOfTheCaller_ExceptEntriesWhosePushIsStillQueued()
    {
        var db = TestDb.Create();
        var me = await TestDb.AddUserAsync(db);
        var other = await TestDb.AddUserAsync(db);
        var queued = Entry(me.Id, PushDeliveryState.Pending);
        db.Notifications.AddRange(
            Entry(me.Id), Entry(me.Id, PushDeliveryState.NotWanted), Entry(me.Id, PushDeliveryState.Failed, read: true), queued, Entry(other.Id));
        await db.SaveChangesAsync();

        await new ClearNotificationsCommandHandler(db).Handle(new ClearNotificationsCommand(me.Id), CancellationToken.None);

        // The queued one is how the user finds out; wiping it unseen would swallow the notification.
        Assert.Equal(new[] { queued.Id }, db.Notifications.Where(n => n.UserId == me.Id).Select(n => n.Id).ToArray());
        Assert.Single(db.Notifications.Where(n => n.UserId == other.Id));
    }

    [Fact]
    public async Task Clear_OnAnEmptyInbox_IsFine()
    {
        var db = TestDb.Create();
        var me = await TestDb.AddUserAsync(db);

        await new ClearNotificationsCommandHandler(db).Handle(new ClearNotificationsCommand(me.Id), CancellationToken.None);

        Assert.Empty(db.Notifications);
    }

    // ---------- mark ticked as read ----------

    [Fact]
    public async Task MarkSelectedRead_TouchesOnlyTheTickedUnreadEntriesOfTheCaller()
    {
        var db = TestDb.Create();
        var me = await TestDb.AddUserAsync(db);
        var other = await TestDb.AddUserAsync(db);
        var ticked = Entry(me.Id);
        var alreadyRead = Entry(me.Id, read: true);
        var firstReadAt = alreadyRead.ReadAtUtc;
        var notTicked = Entry(me.Id);
        var theirs = Entry(other.Id);
        db.Notifications.AddRange(ticked, alreadyRead, notTicked, theirs);
        await db.SaveChangesAsync();

        await new MarkSelectedNotificationsReadCommandHandler(db).Handle(
            new MarkSelectedNotificationsReadCommand(me.Id, new[] { ticked.Id, alreadyRead.Id, theirs.Id }), CancellationToken.None);

        Assert.NotNull(db.Notifications.Single(n => n.Id == ticked.Id).ReadAtUtc);
        Assert.Equal(firstReadAt, db.Notifications.Single(n => n.Id == alreadyRead.Id).ReadAtUtc); // the first read time is kept
        Assert.Null(db.Notifications.Single(n => n.Id == notTicked.Id).ReadAtUtc);
        Assert.Null(db.Notifications.Single(n => n.Id == theirs.Id).ReadAtUtc);
    }
}
