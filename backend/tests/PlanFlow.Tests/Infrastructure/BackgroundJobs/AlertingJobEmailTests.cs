using Moq;
using PlanFlow.Application.Notifications;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.BackgroundJobs;
using PlanFlow.Infrastructure.Persistence;
using PlanFlow.Tests.Application;
using Quartz;

namespace PlanFlow.Tests.Infrastructure.BackgroundJobs;

/// <summary>The urgency alert now also e-mails the assignee, but only once a day per task and only if they want it.</summary>
public class AlertingJobEmailTests
{
    private static Mock<IJobExecutionContext> JobContext()
    {
        var context = new Mock<IJobExecutionContext>();
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context;
    }

    private static AlertingJob Job(PlanFlowDbContext db) =>
        new(db, new NotificationService(db), Mock.Of<Microsoft.Extensions.Logging.ILogger<AlertingJob>>());

    private static async Task<(PlanFlowDbContext Db, User User)> UrgentTaskAsync()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db, "Ann");
        var team = await TestDb.AddTeamAsync(db, (user, TeamRole.Owner));
        db.CalendarItems.Add(new CalendarItem
        {
            TeamId = team.Id, Title = "Submit thesis", AssignedUserId = user.Id,
            DeadlineUtc = DateTime.UtcNow.AddHours(5), CurrentUrgencyScore = 0.92
        });
        await db.SaveChangesAsync();
        return (db, user);
    }

    [Fact]
    public async Task Execute_UrgentTask_RaisesAlertAndMailsTheAssignee()
    {
        var (db, user) = await UrgentTaskAsync();

        await Job(db).Execute(JobContext().Object);

        Assert.Single(db.Alerts);
        var mail = Assert.Single(db.EmailOutbox);
        Assert.Equal(NotificationEvent.TaskUrgent, mail.Event);
        Assert.Equal(user.Email, mail.ToEmail);
        Assert.Contains("Submit thesis", mail.TextBody);
    }

    [Fact]
    public async Task Execute_UnverifiedTask_NeverRaisesAnAlertOrMail()
    {
        // An imported item whose impact / effort are still placeholders: its score is a guess, so it must stay silent.
        var (db, _) = await UrgentTaskAsync();
        db.CalendarItems.Single().IsDataIncomplete = true;
        await db.SaveChangesAsync();

        await Job(db).Execute(JobContext().Object);

        Assert.Empty(db.Alerts);
        Assert.Empty(db.EmailOutbox);
    }

    [Fact]
    public async Task Execute_RunAgain_DoesNotMailAgain()
    {
        var (db, _) = await UrgentTaskAsync();

        await Job(db).Execute(JobContext().Object);
        await Job(db).Execute(JobContext().Object);

        Assert.Single(db.Alerts);
        Assert.Single(db.EmailOutbox);
    }

    [Fact]
    public async Task Execute_AlertRepeatsAfterSixHours_ButTheMailWaitsAFullDay()
    {
        var (db, _) = await UrgentTaskAsync();
        await Job(db).Execute(JobContext().Object);
        db.Alerts.Single().CreatedAtUtc = DateTime.UtcNow.AddHours(-7); // old enough for a new alert, not for a new mail
        await db.SaveChangesAsync();

        await Job(db).Execute(JobContext().Object);

        Assert.Equal(2, db.Alerts.Count());
        Assert.Single(db.EmailOutbox);
    }

    [Fact]
    public async Task Execute_AfterADay_MailsAgain()
    {
        var (db, _) = await UrgentTaskAsync();
        await Job(db).Execute(JobContext().Object);
        db.Alerts.Single().CreatedAtUtc = DateTime.UtcNow.AddHours(-25);
        await db.SaveChangesAsync();

        await Job(db).Execute(JobContext().Object);

        Assert.Equal(2, db.EmailOutbox.Count());
    }

    [Fact]
    public async Task Execute_UrgencyMailsSwitchedOff_StillRaisesTheInAppAlert()
    {
        var (db, user) = await UrgentTaskAsync();
        db.NotificationPreferences.Add(new NotificationPreference { UserId = user.Id, TaskUrgency = false });
        await db.SaveChangesAsync();

        await Job(db).Execute(JobContext().Object);

        Assert.Single(db.Alerts);
        Assert.Empty(db.EmailOutbox);
    }

    [Fact]
    public async Task Execute_TeamMuted_NoMail()
    {
        var (db, _) = await UrgentTaskAsync();
        db.TeamMembers.Single().MuteNotifications = true;
        await db.SaveChangesAsync();

        await Job(db).Execute(JobContext().Object);

        Assert.Empty(db.EmailOutbox);
    }
}
