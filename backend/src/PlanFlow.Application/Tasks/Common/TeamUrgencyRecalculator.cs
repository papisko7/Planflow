using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Caching;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Application.Tasks.Common;

/// <summary>
/// The single place that refreshes stored urgency scores. A task's score depends on its neighbours
/// (a blocker inherits urgency, a blocked task is lowered), so whenever a task is created, edited,
/// finished, deleted or synced, the whole team is rescored in one pass instead of just that task.
/// The hourly job calls the same code, so time passing (deadlines approaching) is covered too.
/// </summary>
public static class TeamUrgencyRecalculator
{
    /// <summary>Score changes smaller than this don't get an audit row, so the history isn't flooded by no-ops.</summary>
    private const double LogChangeThreshold = 0.001;

    /// <summary>
    /// Rescores one team's active tasks and stages the changes on <paramref name="context"/> (the caller saves).
    /// <paramref name="subject"/> is the task the user just touched: it always gets an audit row and is scored
    /// from its in-memory state, so a brand-new or just-edited task is included before it reaches the database.
    /// </summary>
    /// <returns>Ids of the tasks whose stored score changed, for cache invalidation.</returns>
    public static async Task<IReadOnlyCollection<Guid>> RecalculateTeamAsync(
        IApplicationDbContext context, Guid teamId, DateTime nowUtc, ScoreTriggerSource triggerSource,
        CalendarItem? subject, CancellationToken cancellationToken)
    {
        var teamTasks = (await context.CalendarItems.Where(t => t.TeamId == teamId).ToListAsync(cancellationToken)).ToList();
        if (subject is not null && !teamTasks.Contains(subject))
        {
            teamTasks.Add(subject); // newly created, not yet in the database
        }

        return Apply(context, teamTasks, nowUtc, triggerSource, subject);
    }

    /// <summary>Same as <see cref="RecalculateTeamAsync"/> for tasks the caller has already loaded (any teams).</summary>
    public static IReadOnlyCollection<Guid> Apply(
        IApplicationDbContext context, IEnumerable<CalendarItem> tasks, DateTime nowUtc, ScoreTriggerSource triggerSource,
        CalendarItem? subject = null)
    {
        var changed = new List<Guid>();

        foreach (var teamTasks in tasks.GroupBy(t => t.TeamId))
        {
            var active = teamTasks.Where(IsActive).ToList();
            var logs = CalendarItemUrgencyScoreFactory.BuildTeamScoreLogs(active, nowUtc, triggerSource);

            foreach (var task in active)
            {
                var log = logs[task.Id];
                var scoreChanged = Math.Abs(log.FinalScore - task.CurrentUrgencyScore) > LogChangeThreshold;
                if (scoreChanged || task == subject)
                {
                    Stage(context, task, log, nowUtc, changed);
                }
            }

            // A finished subject isn't part of the active graph (it blocks nobody), but an edit still records its score.
            if (subject is not null && subject.TeamId == teamTasks.Key && !IsActive(subject))
            {
                var log = CalendarItemUrgencyScoreFactory.BuildScoreLog(subject, 0.0, false, nowUtc, triggerSource);
                Stage(context, subject, log, nowUtc, changed);
            }
        }

        return changed;
    }

    /// <summary>Drops the cached task list of the team and the cached urgency of every task whose score changed.</summary>
    public static async Task InvalidateCachesAsync(
        ICacheService cache, Guid teamId, IEnumerable<Guid> changedTaskIds, CancellationToken cancellationToken)
    {
        await cache.RemoveAsync(CacheKeys.TeamTasks(teamId), cancellationToken);
        foreach (var taskId in changedTaskIds)
        {
            await cache.RemoveAsync(CacheKeys.TaskUrgency(taskId), cancellationToken);
        }
    }

    private static bool IsActive(CalendarItem task) => task.Status is not (TaskStatus.Done or TaskStatus.Cancelled);

    private static void Stage(
        IApplicationDbContext context, CalendarItem task, UrgencyScoreLog log, DateTime nowUtc, List<Guid> changed)
    {
        task.CurrentUrgencyScore = log.FinalScore;
        task.UpdatedAtUtc = nowUtc;
        context.UrgencyScoreLogs.Add(log);
        changed.Add(task.Id);
    }
}
