using PlanFlow.Application.Notifications;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Tests.Application.Notifications;

/// <summary>Checks the pipeline from "something happened" to "a row in the outbox" — who is skipped and why.</summary>
public class NotificationServiceTests
{
    private static Func<string, EmailContent> Content(string subject = "Subject") =>
        name => new EmailContent(subject, $"<p>Hi {name}</p>", $"Hi {name}");

    [Fact]
    public async Task Notify_QueuesOneMailPerRecipient_WithTheirOwnGreeting()
    {
        var db = TestDb.Create();
        var ann = await TestDb.AddUserAsync(db, "Ann");
        var bob = await TestDb.AddUserAsync(db, "Bob");

        await new NotificationService(db).NotifyAsync(NotificationEvent.TaskChanged, new[] { ann.Id, bob.Id }, Content());
        await db.SaveChangesAsync();

        Assert.Equal(2, db.EmailOutbox.Count());
        Assert.Contains(db.EmailOutbox, m => m.ToEmail == ann.Email && m.TextBody == "Hi Ann");
        Assert.Contains(db.EmailOutbox, m => m.ToEmail == bob.Email && m.TextBody == "Hi Bob");
    }

    [Fact]
    public async Task Notify_NeverMailsTheActor()
    {
        var db = TestDb.Create();
        var actor = await TestDb.AddUserAsync(db);
        var other = await TestDb.AddUserAsync(db);

        await new NotificationService(db).NotifyAsync(
            NotificationEvent.TaskChanged, new[] { actor.Id, other.Id }, Content(), actorUserId: actor.Id);
        await db.SaveChangesAsync();

        Assert.Equal(other.Email, Assert.Single(db.EmailOutbox).ToEmail);
    }

    [Fact]
    public async Task Notify_ListedTwice_StillOneMail()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);

        await new NotificationService(db).NotifyAsync(NotificationEvent.TaskChanged, new[] { user.Id, user.Id }, Content());
        await db.SaveChangesAsync();

        Assert.Single(db.EmailOutbox);
    }

    [Fact]
    public async Task Notify_UserWithoutSettingsRow_GetsTheDefaults()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);

        await new NotificationService(db).NotifyAsync(NotificationEvent.TaskChanged, new[] { user.Id }, Content());   // default on
        await new NotificationService(db).NotifyAsync(NotificationEvent.TeamTaskAdded, new[] { user.Id }, Content()); // default off
        await db.SaveChangesAsync();

        Assert.Equal(NotificationEvent.TaskChanged, Assert.Single(db.EmailOutbox).Event);
    }

    [Fact]
    public async Task Notify_MasterSwitchOff_SkipsOptionalMailButNotMandatoryOnes()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        db.NotificationPreferences.Add(new NotificationPreference { UserId = user.Id, EmailEnabled = false });
        await db.SaveChangesAsync();
        var service = new NotificationService(db);

        await service.NotifyAsync(NotificationEvent.TaskChanged, new[] { user.Id }, Content());
        await service.NotifyAsync(NotificationEvent.PasswordChanged, new[] { user.Id }, Content());
        await db.SaveChangesAsync();

        Assert.Equal(NotificationEvent.PasswordChanged, Assert.Single(db.EmailOutbox).Event);
    }

    [Fact]
    public async Task Notify_CategorySwitchOff_SkipsOnlyThatCategory()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        db.NotificationPreferences.Add(new NotificationPreference { UserId = user.Id, TaskChanges = false });
        await db.SaveChangesAsync();
        var service = new NotificationService(db);

        await service.NotifyAsync(NotificationEvent.TaskChanged, new[] { user.Id }, Content());
        await service.NotifyAsync(NotificationEvent.TaskAssignedToYou, new[] { user.Id }, Content());
        await db.SaveChangesAsync();

        Assert.Equal(NotificationEvent.TaskAssignedToYou, Assert.Single(db.EmailOutbox).Event);
    }

    [Fact]
    public async Task Notify_TeamMuted_SkipsTaskMailsFromThatTeamOnly()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        var mutedTeam = await TestDb.AddTeamAsync(db, (user, TeamRole.Member));
        var openTeam = await TestDb.AddTeamAsync(db, (user, TeamRole.Member));
        db.TeamMembers.Single(m => m.TeamId == mutedTeam.Id).MuteNotifications = true;
        await db.SaveChangesAsync();
        var service = new NotificationService(db);

        await service.NotifyAsync(NotificationEvent.TaskChanged, new[] { user.Id }, Content("muted"), teamId: mutedTeam.Id);
        await service.NotifyAsync(NotificationEvent.TaskChanged, new[] { user.Id }, Content("open"), teamId: openTeam.Id);
        await db.SaveChangesAsync();

        Assert.Equal("open", Assert.Single(db.EmailOutbox).Subject);
    }

    [Fact]
    public async Task Notify_TeamMuted_StillDeliversMembershipMails()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (user, TeamRole.Member));
        db.TeamMembers.Single().MuteNotifications = true;
        db.TeamMembers.Single().MuteChat = true;
        await db.SaveChangesAsync();

        await new NotificationService(db).NotifyAsync(NotificationEvent.TeamRoleChanged, new[] { user.Id }, Content(), teamId: team.Id);
        await db.SaveChangesAsync();

        Assert.Single(db.EmailOutbox);
    }

    [Fact]
    public async Task Notify_DoesNotSaveByItself()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);

        await new NotificationService(db).NotifyAsync(NotificationEvent.TaskChanged, new[] { user.Id }, Content());

        // Queued in the context but not committed: the caller's SaveChanges decides (outbox pattern).
        var entry = Assert.Single(db.ChangeTracker.Entries<EmailOutboxMessage>());
        Assert.Equal(Microsoft.EntityFrameworkCore.EntityState.Added, entry.State);
    }

    [Fact]
    public void QueueDirect_IgnoresPreferences()
    {
        var db = TestDb.Create();

        new NotificationService(db).QueueDirect(NotificationEvent.Welcome, "new@example.com", "New", Content());
        db.SaveChanges();

        Assert.Equal("new@example.com", Assert.Single(db.EmailOutbox).ToEmail);
    }

    [Fact]
    public void Layout_HtmlEncodesUserSuppliedText()
    {
        var content = EmailTemplates.TaskDeleted("<script>alert(1)</script>", "Team & Co", "Ann")("Bob");

        Assert.DoesNotContain("<script>", content.HtmlBody);
        Assert.Contains("&lt;script&gt;", content.HtmlBody);
        Assert.Contains("Team &amp; Co", content.HtmlBody);
    }

    [Fact]
    public void Subject_LineBreaksInATitle_AreRemoved()
    {
        var content = EmailTemplates.TaskAssigned("Line one\r\nBcc: evil@example.com", "Team", "Ann", null)("Bob");

        Assert.DoesNotContain('\n', content.Subject);
        Assert.DoesNotContain('\r', content.Subject);
    }
}
