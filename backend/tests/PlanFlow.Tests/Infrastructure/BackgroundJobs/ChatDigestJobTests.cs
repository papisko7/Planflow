using Moq;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.BackgroundJobs;
using PlanFlow.Infrastructure.Persistence;
using PlanFlow.Tests.Application;
using Quartz;

namespace PlanFlow.Tests.Infrastructure.BackgroundJobs;

/// <summary>The chat digest: one summary mail for messages missed while offline, at most hourly, never for people who are online.</summary>
public class ChatDigestJobTests
{
    private static Mock<IJobExecutionContext> JobContext()
    {
        var context = new Mock<IJobExecutionContext>();
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context;
    }

    private static ChatDigestJob Job(PlanFlowDbContext db, bool listenerOnline = false)
    {
        var presence = new Mock<IChatPresence>();
        presence.Setup(p => p.IsOnlineAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(listenerOnline);
        return new ChatDigestJob(db, presence.Object, new NotificationService(db), Mock.Of<Microsoft.Extensions.Logging.ILogger<ChatDigestJob>>());
    }

    /// <summary>Ann subscribes to the digest; Bob writes in their shared team.</summary>
    private static async Task<(PlanFlowDbContext Db, User Ann, User Bob, Team Team, NotificationPreference Prefs)> SetUpAsync(bool subscribe = true)
    {
        var db = TestDb.Create();
        var ann = await TestDb.AddUserAsync(db, "Ann");
        var bob = await TestDb.AddUserAsync(db, "Bob");
        var team = await TestDb.AddTeamAsync(db, (ann, TeamRole.Owner), (bob, TeamRole.Member));
        team.Name = "Thesis";
        var prefs = new NotificationPreference
        {
            UserId = ann.Id, ChatDigest = subscribe, ChatSeenUpToUtc = DateTime.UtcNow.AddHours(-3)
        };
        db.NotificationPreferences.Add(prefs);
        await db.SaveChangesAsync();
        return (db, ann, bob, team, prefs);
    }

    private static void Say(PlanFlowDbContext db, Team team, User sender, int count = 1, DateTime? at = null)
    {
        for (var i = 0; i < count; i++)
        {
            db.ChatMessages.Add(new ChatMessage { TeamId = team.Id, SenderUserId = sender.Id, Content = "hi", CreatedAtUtc = at ?? DateTime.UtcNow.AddMinutes(-10) });
        }

        db.SaveChanges();
    }

    [Fact]
    public async Task Execute_OfflineSubscriberWithMissedMessages_GetsOneSummaryMail()
    {
        var (db, ann, bob, team, _) = await SetUpAsync();
        Say(db, team, bob, count: 3);

        await Job(db).Execute(JobContext().Object);

        var mail = Assert.Single(db.EmailOutbox);
        Assert.Equal(NotificationEvent.ChatDigest, mail.Event);
        Assert.Equal(ann.Email, mail.ToEmail);
        Assert.Contains("3 new messages", mail.TextBody);
        Assert.Contains("Thesis", mail.TextBody);
    }

    [Fact]
    public async Task Execute_OnlineSubscriber_GetsNothing_AndTheirMessagesCountAsSeen()
    {
        var (db, _, bob, team, prefs) = await SetUpAsync();
        Say(db, team, bob, count: 2);

        await Job(db, listenerOnline: true).Execute(JobContext().Object);
        await Job(db, listenerOnline: false).Execute(JobContext().Object); // later, now offline

        Assert.Empty(db.EmailOutbox);
        Assert.True(prefs.ChatSeenUpToUtc > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Execute_NotSubscribed_GetsNothing()
    {
        var (db, _, bob, team, _) = await SetUpAsync(subscribe: false);
        Say(db, team, bob);

        await Job(db).Execute(JobContext().Object);

        Assert.Empty(db.EmailOutbox);
    }

    [Fact]
    public async Task Execute_MasterSwitchOff_GetsNothing()
    {
        var (db, _, bob, team, prefs) = await SetUpAsync();
        prefs.EmailEnabled = false;
        await db.SaveChangesAsync();
        Say(db, team, bob);

        await Job(db).Execute(JobContext().Object);

        Assert.Empty(db.EmailOutbox);
    }

    [Fact]
    public async Task Execute_OwnMessagesDoNotCount()
    {
        var (db, ann, _, team, _) = await SetUpAsync();
        Say(db, team, ann, count: 4);

        await Job(db).Execute(JobContext().Object);

        Assert.Empty(db.EmailOutbox);
    }

    [Fact]
    public async Task Execute_MessagesBeforeTheSeenMark_AreIgnored()
    {
        var (db, _, bob, team, _) = await SetUpAsync();
        Say(db, team, bob, count: 2, at: DateTime.UtcNow.AddHours(-5));

        await Job(db).Execute(JobContext().Object);

        Assert.Empty(db.EmailOutbox);
    }

    [Fact]
    public async Task Execute_ChatMutedTeam_IsLeftOut()
    {
        var (db, ann, bob, team, _) = await SetUpAsync();
        db.TeamMembers.Single(m => m.UserId == ann.Id).MuteChat = true;
        await db.SaveChangesAsync();
        Say(db, team, bob);

        await Job(db).Execute(JobContext().Object);

        Assert.Empty(db.EmailOutbox);
    }

    [Fact]
    public async Task Execute_AfterADigest_NoSecondOneWithinTheHour_ThenOneForNewMessages()
    {
        var (db, _, bob, team, prefs) = await SetUpAsync();
        Say(db, team, bob);
        await Job(db).Execute(JobContext().Object);
        Say(db, team, bob, at: DateTime.UtcNow.AddMinutes(1));

        await Job(db).Execute(JobContext().Object); // too soon
        Assert.Single(db.EmailOutbox);

        prefs.LastChatDigestSentAtUtc = DateTime.UtcNow.AddHours(-2);
        prefs.ChatSeenUpToUtc = DateTime.UtcNow.AddMinutes(-5);
        await db.SaveChangesAsync();
        Say(db, team, bob, at: DateTime.UtcNow.AddMinutes(-1));
        await Job(db).Execute(JobContext().Object);

        Assert.Equal(2, db.EmailOutbox.Count());
    }

    [Fact]
    public async Task Execute_MessagesAlreadySummarised_AreNotSentTwice()
    {
        var (db, _, bob, team, prefs) = await SetUpAsync();
        Say(db, team, bob);
        await Job(db).Execute(JobContext().Object);
        prefs.LastChatDigestSentAtUtc = DateTime.UtcNow.AddHours(-2); // the hour has passed, but nothing new was said

        await Job(db).Execute(JobContext().Object);

        Assert.Single(db.EmailOutbox);
    }
}
