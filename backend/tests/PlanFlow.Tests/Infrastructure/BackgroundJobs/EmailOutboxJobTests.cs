using Moq;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.BackgroundJobs;
using PlanFlow.Tests.Application;
using Quartz;

namespace PlanFlow.Tests.Infrastructure.BackgroundJobs;

/// <summary>The outbox job is the only thing that talks to the mail server: it delivers, retries with a delay, and gives up after a few tries.</summary>
public class EmailOutboxJobTests
{
    private static Mock<IJobExecutionContext> JobContext()
    {
        var context = new Mock<IJobExecutionContext>();
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context;
    }

    private static EmailOutboxMessage Queued(string to = "ann@example.com", int attempts = 0, DateTime? due = null) => new()
    {
        ToEmail = to, Subject = "Hello", HtmlBody = "<p>Hello</p>", TextBody = "Hello",
        Event = NotificationEvent.Welcome, Attempts = attempts, NextAttemptAtUtc = due ?? DateTime.UtcNow.AddSeconds(-1)
    };

    private static EmailOutboxJob Job(PlanFlow.Infrastructure.Persistence.PlanFlowDbContext db, Mock<IEmailSender> sender) =>
        new(db, sender.Object, Mock.Of<Microsoft.Extensions.Logging.ILogger<EmailOutboxJob>>());

    private static void VerifyNothingSent(Mock<IEmailSender> sender) =>
        sender.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task Execute_SendsDueMailAndMarksItSent()
    {
        var db = TestDb.Create();
        db.EmailOutbox.Add(Queued());
        await db.SaveChangesAsync();
        var sender = new Mock<IEmailSender>();

        await Job(db, sender).Execute(JobContext().Object);

        sender.Verify(s => s.SendAsync("ann@example.com", "Hello", "<p>Hello</p>", "Hello", It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(db.EmailOutbox.Single().SentAtUtc);
    }

    [Fact]
    public async Task Execute_AlreadySentMail_IsNotSentAgain()
    {
        var db = TestDb.Create();
        var sent = Queued();
        sent.SentAtUtc = DateTime.UtcNow.AddMinutes(-1);
        db.EmailOutbox.Add(sent);
        await db.SaveChangesAsync();
        var sender = new Mock<IEmailSender>();

        await Job(db, sender).Execute(JobContext().Object);

        VerifyNothingSent(sender);
    }

    [Fact]
    public async Task Execute_MailNotDueYet_IsLeftAlone()
    {
        var db = TestDb.Create();
        db.EmailOutbox.Add(Queued(due: DateTime.UtcNow.AddMinutes(5)));
        await db.SaveChangesAsync();
        var sender = new Mock<IEmailSender>();

        await Job(db, sender).Execute(JobContext().Object);

        VerifyNothingSent(sender);
    }

    [Fact]
    public async Task Execute_ServerFails_KeepsTheMailAndSchedulesARetry()
    {
        var db = TestDb.Create();
        db.EmailOutbox.Add(Queued());
        await db.SaveChangesAsync();
        var sender = new Mock<IEmailSender>();
        sender.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP down"));

        await Job(db, sender).Execute(JobContext().Object);

        var row = db.EmailOutbox.Single();
        Assert.Null(row.SentAtUtc);
        Assert.Equal(1, row.Attempts);
        Assert.Equal("SMTP down", row.LastError);
        Assert.True(row.NextAttemptAtUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task Execute_OneBadMailDoesNotBlockTheOthers()
    {
        var db = TestDb.Create();
        db.EmailOutbox.Add(Queued("bad@example.com"));
        db.EmailOutbox.Add(Queued("good@example.com"));
        await db.SaveChangesAsync();
        var sender = new Mock<IEmailSender>();
        sender.Setup(s => s.SendAsync("bad@example.com", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("rejected"));

        await Job(db, sender).Execute(JobContext().Object);

        Assert.NotNull(db.EmailOutbox.Single(m => m.ToEmail == "good@example.com").SentAtUtc);
        Assert.Null(db.EmailOutbox.Single(m => m.ToEmail == "bad@example.com").SentAtUtc);
    }

    [Fact]
    public async Task Execute_GivesUpAfterTheMaximumNumberOfAttempts()
    {
        var db = TestDb.Create();
        db.EmailOutbox.Add(Queued(attempts: EmailOutboxJob.MaxAttempts));
        await db.SaveChangesAsync();
        var sender = new Mock<IEmailSender>();

        await Job(db, sender).Execute(JobContext().Object);

        VerifyNothingSent(sender);
    }
}
