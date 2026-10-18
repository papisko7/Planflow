using Moq;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.BackgroundJobs;
using PlanFlow.Tests.Application;
using Quartz;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Infrastructure.BackgroundJobs;

/// <summary>Step 7.5.6: the next occurrence of a recurring item keeps its alerts (as fresh ones) and the people taking part.</summary>
public class RecurringItemAlertsTests
{
    [Fact]
    public async Task Execute_NextInstance_CarriesTheAlertsAndTheParticipants_ButNotTheMemoryOfHavingGoneOff()
    {
        var db = TestDb.Create();
        var anna = await TestDb.AddUserAsync(db, "Anna");
        var ben = await TestDb.AddUserAsync(db, "Ben");
        var team = await TestDb.AddTeamAsync(db, (anna, TeamRole.Owner), (ben, TeamRole.Member));
        var now = DateTime.UtcNow;
        var item = new CalendarItem
        {
            TeamId = team.Id, AssignedUserId = anna.Id, Title = "Weekly report", Status = TaskStatus.Completed,
            IsRecurring = true, RecurrenceIntervalHours = 24 * 7, StartUtc = now.AddHours(-20), DeadlineUtc = now.AddDays(1), ImpactScore = 6
        };
        db.CalendarItems.Add(item);
        db.ItemReminders.AddRange(
            new ItemReminder { CalendarItemId = item.Id, Anchor = ReminderAnchor.Deadline, OffsetMinutes = 60, LastFiredForUtc = now.AddHours(-1) },
            new ItemReminder { CalendarItemId = item.Id, DaysBefore = 1, TimeOfDayMinutes = 540 });
        db.ItemParticipants.Add(new ItemParticipant { CalendarItemId = item.Id, UserId = ben.Id, Role = ParticipantRole.CoContractor });
        await db.SaveChangesAsync();

        var context = new Mock<IJobExecutionContext>();
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        await new RecurringItemJob(db, Mock.Of<ICacheService>(), Mock.Of<Microsoft.Extensions.Logging.ILogger<RecurringItemJob>>())
            .Execute(context.Object);

        var next = Assert.Single(db.CalendarItems, t => t.Id != item.Id);
        var reminders = db.ItemReminders.Where(r => r.CalendarItemId == next.Id).ToList();
        Assert.Equal(2, reminders.Count);
        Assert.Contains(reminders, r => r is { Anchor: ReminderAnchor.Deadline, OffsetMinutes: 60, LastFiredForUtc: null });
        Assert.Contains(reminders, r => r is { Anchor: ReminderAnchor.Start, DaysBefore: 1, TimeOfDayMinutes: 540 });
        var participant = Assert.Single(db.ItemParticipants, p => p.CalendarItemId == next.Id);
        Assert.Equal(ben.Id, participant.UserId);
        Assert.Equal(ParticipantRole.CoContractor, participant.Role);
    }
}
