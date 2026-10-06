using Moq;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.BackgroundJobs;
using PlanFlow.Infrastructure.Persistence;
using PlanFlow.Tests.Application;
using Quartz;

namespace PlanFlow.Tests.Infrastructure.BackgroundJobs;

/// <summary>The push job is the only thing that talks to the push service: it delivers, retries with a delay, gives up after a few tries and forgets dead phones.</summary>
public class PushDeliveryJobTests
{
    private const string Phone = "ExponentPushToken[phone-1]";

    private static IJobExecutionContext JobContext()
    {
        var context = new Mock<IJobExecutionContext>();
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    private static PushDeliveryJob Job(PlanFlowDbContext db, Mock<IPushSender> sender) =>
        new(db, sender.Object, Mock.Of<Microsoft.Extensions.Logging.ILogger<PushDeliveryJob>>());

    private static Notification Pending(Guid userId, int attempts = 0, DateTime? due = null) => new()
    {
        UserId = userId, Event = NotificationEvent.TaskAssignedToYou, Title = "New task", Body = "Ann assigned it to you.",
        PushState = PushDeliveryState.Pending, PushAttempts = attempts, PushNextAttemptAtUtc = due ?? DateTime.UtcNow.AddSeconds(-1),
        TeamId = Guid.NewGuid(), TaskId = Guid.NewGuid()
    };

    private static Mock<IPushSender> SenderAnswering(params PushResult[] results)
    {
        var sender = new Mock<IPushSender>();
        sender.Setup(s => s.SendAsync(It.IsAny<IReadOnlyList<PushMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(results);
        return sender;
    }

    private static async Task<(PlanFlowDbContext Db, User User)> UserWithPhoneAsync()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        db.PushTokens.Add(new PushToken { UserId = user.Id, Token = Phone, Platform = "ios" });
        await db.SaveChangesAsync();
        return (db, user);
    }

    [Fact]
    public async Task Execute_SendsThePushWithTextTargetAndUnreadBadge_AndMarksItSent()
    {
        var (db, user) = await UserWithPhoneAsync();
        var entry = Pending(user.Id);
        db.Notifications.AddRange(entry, new Notification { UserId = user.Id, Title = "older", Body = "x" }); // 2 unread in total
        await db.SaveChangesAsync();
        var sender = SenderAnswering(new PushResult(true, false, null));

        await Job(db, sender).Execute(JobContext());

        sender.Verify(s => s.SendAsync(
            It.Is<IReadOnlyList<PushMessage>>(m =>
                m.Count == 1 && m[0].To == Phone && m[0].Title == "New task" && m[0].Body == "Ann assigned it to you." && m[0].Badge == 2
                && m[0].Data["notificationId"] == entry.Id.ToString() && m[0].Data["taskId"] == entry.TaskId!.Value.ToString()
                && m[0].Data["teamId"] == entry.TeamId!.Value.ToString() && m[0].Data["event"] == ((int)NotificationEvent.TaskAssignedToYou).ToString()),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(PushDeliveryState.Sent, db.Notifications.Single(n => n.Id == entry.Id).PushState);
    }

    [Fact]
    public async Task Execute_UserWithoutAPhone_IsDoneWithoutCallingThePushService()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        db.Notifications.Add(Pending(user.Id));
        await db.SaveChangesAsync();
        var sender = new Mock<IPushSender>();

        await Job(db, sender).Execute(JobContext());

        sender.VerifyNoOtherCalls();
        Assert.Equal(PushDeliveryState.Sent, db.Notifications.Single().PushState);
    }

    [Fact]
    public async Task Execute_SendsToEveryPhoneOfTheUser()
    {
        var (db, user) = await UserWithPhoneAsync();
        db.PushTokens.Add(new PushToken { UserId = user.Id, Token = "ExponentPushToken[tablet-1]", Platform = "android" });
        db.Notifications.Add(Pending(user.Id));
        await db.SaveChangesAsync();
        var sender = SenderAnswering(new PushResult(true, false, null), new PushResult(true, false, null));

        await Job(db, sender).Execute(JobContext());

        sender.Verify(s => s.SendAsync(It.Is<IReadOnlyList<PushMessage>>(m => m.Count == 2), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_NotificationsThatAreNotPendingOrNotDueYet_AreLeftAlone()
    {
        var (db, user) = await UserWithPhoneAsync();
        var notWanted = Pending(user.Id);
        notWanted.PushState = PushDeliveryState.NotWanted;
        var alreadySent = Pending(user.Id);
        alreadySent.PushState = PushDeliveryState.Sent;
        db.Notifications.AddRange(notWanted, alreadySent, Pending(user.Id, attempts: 1, due: DateTime.UtcNow.AddMinutes(5)));
        await db.SaveChangesAsync();
        var sender = new Mock<IPushSender>();

        await Job(db, sender).Execute(JobContext());

        sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Execute_PushServiceDown_RetriesLaterWithGrowingDelay_ThenGivesUp()
    {
        var (db, user) = await UserWithPhoneAsync();
        db.Notifications.Add(Pending(user.Id));
        await db.SaveChangesAsync();
        var sender = new Mock<IPushSender>();
        sender.Setup(s => s.SendAsync(It.IsAny<IReadOnlyList<PushMessage>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("down"));
        var job = Job(db, sender);

        await job.Execute(JobContext());

        var entry = db.Notifications.Single();
        Assert.Equal(PushDeliveryState.Pending, entry.PushState);
        Assert.Equal(1, entry.PushAttempts);
        Assert.True(entry.PushNextAttemptAtUtc > DateTime.UtcNow);
        Assert.Equal("down", entry.PushLastError);

        // Final attempt: the fifth failure turns it into Failed (the inbox entry itself stays).
        entry.PushAttempts = PushDeliveryJob.MaxAttempts - 1;
        entry.PushNextAttemptAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();
        await job.Execute(JobContext());

        Assert.Equal(PushDeliveryState.Failed, entry.PushState);
    }

    [Fact]
    public async Task Execute_DeadPhone_IsForgotten_AndTheNotificationIsNotRetried()
    {
        var (db, user) = await UserWithPhoneAsync();
        db.Notifications.Add(Pending(user.Id));
        await db.SaveChangesAsync();
        var sender = SenderAnswering(new PushResult(false, true, "DeviceNotRegistered"));

        await Job(db, sender).Execute(JobContext());

        Assert.Empty(db.PushTokens);
        Assert.Equal(PushDeliveryState.Sent, db.Notifications.Single().PushState);
    }

    [Fact]
    public async Task Execute_RefusedForAnotherReason_IsRetried_AndTheTokenIsKept()
    {
        var (db, user) = await UserWithPhoneAsync();
        db.Notifications.Add(Pending(user.Id));
        await db.SaveChangesAsync();
        var sender = SenderAnswering(new PushResult(false, false, "MessageRateExceeded"));

        await Job(db, sender).Execute(JobContext());

        Assert.Single(db.PushTokens);
        var entry = db.Notifications.Single();
        Assert.Equal(PushDeliveryState.Pending, entry.PushState);
        Assert.Equal(1, entry.PushAttempts);
        Assert.Equal("MessageRateExceeded", entry.PushLastError);
    }

    [Fact]
    public async Task Execute_TwoPhones_OneDeadOneAlive_CountsAsDelivered()
    {
        var (db, user) = await UserWithPhoneAsync();
        db.PushTokens.Add(new PushToken { UserId = user.Id, Token = "ExponentPushToken[old-phone]", Platform = "ios" });
        db.Notifications.Add(Pending(user.Id));
        await db.SaveChangesAsync();
        var sender = SenderAnswering(new PushResult(true, false, null), new PushResult(false, true, "DeviceNotRegistered"));

        await Job(db, sender).Execute(JobContext());

        Assert.Equal(PushDeliveryState.Sent, db.Notifications.Single().PushState);
        Assert.Single(db.PushTokens);
    }
}
