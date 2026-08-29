using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;
using Xunit;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Application.Tasks.Common;

public class ScoreAuditLoggingTests
{
    private PlanFlowDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<PlanFlowDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new PlanFlowDbContext(options);
    }

    [Fact]
    public void BuildScoreLog_CreatesAuditWithCorrectTriggerSource()
    {
        // Arrange
        var nowUtc = DateTime.UtcNow;
        var team = new Team { Id = Guid.NewGuid(), Name = "Test Team", CreatedAtUtc = nowUtc };
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            Team = team,
            Title = "Test Task",
            Status = TaskStatus.Todo,
            ImpactScore = 5,
            CurrentUrgencyScore = 0.0,
            AiAssessmentScore = null
        };

        // Act
        var scoreLog = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 0, nowUtc, ScoreTriggerSource.ManualCreate);

        // Assert
        Assert.NotNull(scoreLog);
        Assert.Equal(task.Id, scoreLog.TaskItemId);
        Assert.Equal(ScoreTriggerSource.ManualCreate, scoreLog.TriggerSource);
        Assert.True(scoreLog.FinalScore >= 0.0 && scoreLog.FinalScore <= 1.0);
    }

    [Fact]
    public void BuildScoreLog_WithDeadlineUrgency_CapturesComponentValues()
    {
        // Arrange
        var nowUtc = new DateTime(2025, 08, 30, 12, 0, 0, DateTimeKind.Utc);
        var deadlineUtc = nowUtc.AddDays(5); // 5 days out
        var team = new Team { Id = Guid.NewGuid(), Name = "Test Team", CreatedAtUtc = nowUtc };
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            Team = team,
            Title = "Urgent Task",
            Status = TaskStatus.Todo,
            DeadlineUtc = deadlineUtc,
            ImpactScore = 8,
            CurrentUrgencyScore = 0.0,
            AiAssessmentScore = 0.6 // AI gave it a score
        };

        // Act
        var scoreLog = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 0, nowUtc, ScoreTriggerSource.ManualUpdate);

        // Assert
        Assert.True(scoreLog.DeadlineComponent > 0.0, "Deadline component should be > 0 for near-future deadline");
        Assert.Equal(0.6, scoreLog.AiComponent);
        Assert.Equal(0.0, scoreLog.BlockingComponent); // No blocking tasks
        Assert.True(scoreLog.ImpactComponent > 0.0, "Impact component should reflect impact score");
        Assert.False(scoreLog.AiFallbackUsed, "Fallback should be false since AI score is present");
        Assert.Equal(ScoreTriggerSource.ManualUpdate, scoreLog.TriggerSource);
    }

    [Fact]
    public void BuildScoreLog_WithAiFallback_MarksAiFallbackFlag()
    {
        // Arrange
        var nowUtc = DateTime.UtcNow;
        var team = new Team { Id = Guid.NewGuid(), Name = "Test Team", CreatedAtUtc = nowUtc };
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            Team = team,
            Title = "Task Without AI",
            Status = TaskStatus.Todo,
            ImpactScore = 5,
            AiAssessmentScore = null // No AI assessment
        };

        // Act
        var scoreLog = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 0, nowUtc, ScoreTriggerSource.AiFallback);

        // Assert
        Assert.Equal(0.0, scoreLog.AiComponent);
        Assert.True(scoreLog.AiFallbackUsed, "Should mark fallback when AI score is null");
        Assert.Equal(ScoreTriggerSource.AiFallback, scoreLog.TriggerSource);
    }

    [Fact]
    public void BuildScoreLog_WithBlockingTasks_IncludesBlockingComponent()
    {
        // Arrange
        var nowUtc = DateTime.UtcNow;
        var team = new Team { Id = Guid.NewGuid(), Name = "Test Team", CreatedAtUtc = nowUtc };
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            Team = team,
            Title = "Blocking Task",
            Status = TaskStatus.Todo,
            ImpactScore = 5,
            AiAssessmentScore = null
        };

        // Act: This task blocks 3 others
        var scoreLog = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 3, nowUtc, ScoreTriggerSource.ScheduledRecalculation);

        // Assert
        Assert.True(scoreLog.BlockingComponent > 0.0, "Blocking component should reflect 3 blocked tasks");
        Assert.True(scoreLog.BlockingComponent < 1.0, "Blocking should not saturate at 3 tasks (saturation is 5)");
        Assert.Equal(ScoreTriggerSource.ScheduledRecalculation, scoreLog.TriggerSource);
    }

    [Fact]
    public void BuildScoreLog_AllTriggerSources_Persist()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var nowUtc = DateTime.UtcNow;
        var team = new Team { Id = Guid.NewGuid(), Name = "Test Team", CreatedAtUtc = nowUtc };
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            Team = team,
            Title = "Multi-Trigger Task",
            Status = TaskStatus.Todo,
            ImpactScore = 5,
            AiAssessmentScore = null
        };

        context.Add(team);
        context.Add(task);

        var triggers = new[]
        {
            ScoreTriggerSource.ManualCreate,
            ScoreTriggerSource.ManualUpdate,
            ScoreTriggerSource.ScheduledRecalculation,
            ScoreTriggerSource.AiAssessment,
            ScoreTriggerSource.AiFallback
        };

        var logs = new List<UrgencyScoreLog>();

        // Act
        foreach (var trigger in triggers)
        {
            var scoreLog = TaskUrgencyScoreFactory.BuildScoreLog(task, 0, nowUtc, trigger);
            logs.Add(scoreLog);
            context.UrgencyScoreLogs.Add(scoreLog);
        }

        context.SaveChanges();

        // Assert
        Assert.Equal(triggers.Length, logs.Count);
        for (int i = 0; i < logs.Count; i++)
        {
            Assert.Equal(triggers[i], logs[i].TriggerSource);
            Assert.Equal(task.Id, logs[i].TaskItemId);
        }

        // Verify persistence
        var retrievedLogs = context.UrgencyScoreLogs.Where(l => l.TaskItemId == task.Id).OrderBy(l => l.CreatedAtUtc).ToList();
        Assert.Equal(triggers.Length, retrievedLogs.Count);
        for (int i = 0; i < retrievedLogs.Count; i++)
        {
            Assert.Equal(triggers[i], retrievedLogs[i].TriggerSource);
        }

        context.Dispose();
    }

    [Fact]
    public void BuildScoreLog_WithUserOverride_IncludesOverrideComponent()
    {
        // Arrange
        var nowUtc = new DateTime(2025, 08, 30, 12, 0, 0, DateTimeKind.Utc);
        var expiryUtc = nowUtc.AddHours(1);
        var team = new Team { Id = Guid.NewGuid(), Name = "Test Team", CreatedAtUtc = nowUtc };
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            Team = team,
            Title = "Overridden Task",
            Status = TaskStatus.Todo,
            ImpactScore = 5,
            ManualUrgencyOverride = 0.8,
            ManualUrgencyOverrideExpiresAtUtc = expiryUtc,
            AiAssessmentScore = null
        };

        // Act
        var scoreLog = TaskUrgencyScoreFactory.BuildScoreLog(task, 0, nowUtc, ScoreTriggerSource.ManualUpdate);

        // Assert
        Assert.Equal(0.8, scoreLog.UserOverrideComponent);
        Assert.NotEqual(0.0, scoreLog.FinalScore); // Override should influence final score
    }

    [Fact]
    public void BuildScoreLog_WithExpiredOverride_IgnoresOverride()
    {
        // Arrange
        var nowUtc = new DateTime(2025, 08, 30, 12, 0, 0, DateTimeKind.Utc);
        var expiryUtc = nowUtc.AddHours(-1); // Already expired
        var team = new Team { Id = Guid.NewGuid(), Name = "Test Team", CreatedAtUtc = nowUtc };
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            Team = team,
            Title = "Expired Override Task",
            Status = TaskStatus.Todo,
            ImpactScore = 5,
            ManualUrgencyOverride = 0.9,
            ManualUrgencyOverrideExpiresAtUtc = expiryUtc,
            AiAssessmentScore = null
        };

        // Act
        var scoreLog = TaskUrgencyScoreFactory.BuildScoreLog(task, 0, nowUtc, ScoreTriggerSource.ManualUpdate);

        // Assert
        Assert.Equal(0.0, scoreLog.UserOverrideComponent);
    }

    [Fact]
    public void BuildScoreLog_ComponentsPrecision_StoresExactDecimals()
    {
        // Arrange
        var nowUtc = new DateTime(2025, 08, 30, 12, 0, 0, DateTimeKind.Utc);
        var deadlineUtc = nowUtc.AddDays(7.5);
        var team = new Team { Id = Guid.NewGuid(), Name = "Test Team", CreatedAtUtc = nowUtc };
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            Team = team,
            Title = "Precision Test",
            Status = TaskStatus.Todo,
            DeadlineUtc = deadlineUtc,
            ImpactScore = 7,
            AiAssessmentScore = 0.5555 // Specific decimal
        };

        // Act
        var scoreLog = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 2, nowUtc, ScoreTriggerSource.AiAssessment);

        // Assert
        Assert.Equal(0.5555, scoreLog.AiComponent);
        Assert.True(scoreLog.DeadlineComponent > 0.0 && scoreLog.DeadlineComponent < 1.0);
        Assert.True(scoreLog.BlockingComponent > 0.0);
        Assert.True(scoreLog.FinalScore >= 0.0 && scoreLog.FinalScore <= 1.0);
    }

    [Fact]
    public void BuildScoreLog_DefaultTriggerSource_IsManualUpdate()
    {
        // Arrange
        var nowUtc = DateTime.UtcNow;
        var team = new Team { Id = Guid.NewGuid(), Name = "Test Team", CreatedAtUtc = nowUtc };
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            Team = team,
            Title = "Default Trigger Task",
            Status = TaskStatus.Todo,
            ImpactScore = 5,
            AiAssessmentScore = null
        };

        // Act: Call without specifying trigger source (should default to ManualUpdate)
        var scoreLog = TaskUrgencyScoreFactory.BuildScoreLog(task, 0, nowUtc);

        // Assert
        Assert.Equal(ScoreTriggerSource.ManualUpdate, scoreLog.TriggerSource);
    }

    [Fact]
    public void UrgencyScoreLogs_MultiplePerTask_OrderedByCreatedAt()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var nowUtc = new DateTime(2025, 08, 30, 12, 0, 0, DateTimeKind.Utc);
        var team = new Team { Id = Guid.NewGuid(), Name = "Test Team", CreatedAtUtc = nowUtc };
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            Team = team,
            Title = "History Task",
            Status = TaskStatus.Todo,
            ImpactScore = 5,
            AiAssessmentScore = null
        };

        context.Add(team);
        context.Add(task);
        context.SaveChanges();

        // Act: Create multiple score logs at different times
        for (int i = 0; i < 3; i++)
        {
            var timeOffset = nowUtc.AddMinutes(i);
            var scoreLog = TaskUrgencyScoreFactory.BuildScoreLog(task, 0, timeOffset, ScoreTriggerSource.ScheduledRecalculation);
            context.UrgencyScoreLogs.Add(scoreLog);
            context.SaveChanges();
        }

        // Assert
        var logs = context.UrgencyScoreLogs
            .Where(l => l.TaskItemId == task.Id)
            .OrderBy(l => l.CreatedAtUtc)
            .ToList();

        Assert.Equal(3, logs.Count);
        for (int i = 0; i < logs.Count - 1; i++)
        {
            Assert.True(logs[i].CreatedAtUtc <= logs[i + 1].CreatedAtUtc);
        }

        context.Dispose();
    }
}
