using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Caching;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Application.Tasks.Common;

/// <summary>
/// The single place that refreshes stored urgency scores. A task's score depends on its neighbours
/// (a blocker inherits urgency, a blocked task is lowered), so whenever a task is created, edited,
/// finished, deleted or synced, the whole team is rescored in one pass instead of just that task.
/// The hourly job calls the same code, so time passing (deadlines approaching) is covered too.
/// Informational items (Out of office, Focus time, ...) are not ranked: they keep a score of 0 and get no audit rows.
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

        var zones = await UserTimeZones.LoadAsync(context, teamTasks, cancellationToken);
        var absences = await UserAbsences.LoadAsync(context, teamTasks, nowUtc, cancellationToken);
        return Apply(context, teamTasks, nowUtc, triggerSource, subject, zones, absences);
    }

    /// <summary>
    /// Same as <see cref="RecalculateTeamAsync"/> for tasks the caller has already loaded (any teams).
    /// <paramref name="zones"/> are the time zones of the assignees (see <see cref="UserTimeZones"/>); without them all-day items are read in UTC.
    /// <paramref name="absences"/> are their out-of-office spans (see <see cref="UserAbsences"/>); without them no deadline is shortened.
    /// </summary>
    public static IReadOnlyCollection<Guid> Apply(
        IApplicationDbContext context, IEnumerable<CalendarItem> tasks, DateTime nowUtc, ScoreTriggerSource triggerSource,
        CalendarItem? subject = null, IReadOnlyDictionary<Guid, TimeZoneInfo>? zones = null,
        IReadOnlyDictionary<Guid, IReadOnlyList<AbsenceSpan>>? absences = null)
    {
        var changed = new List<Guid>();
        var zoneOf = UserTimeZones.ResolverFor(zones);
        var absencesOf = UserAbsences.ResolverFor(absences);

        foreach (var teamTasks in tasks.GroupBy(t => t.TeamId))
        {
            // Informational items have no score. One that was scored before the algorithm learnt this (version 1) is reset once.
            foreach (var informational in teamTasks.Where(t => !CalendarItemUrgencyScoreFactory.IsRanked(t) && t.CurrentUrgencyScore != 0.0))
            {
                informational.CurrentUrgencyScore = 0.0;
                informational.UpdatedAtUtc = nowUtc;
                changed.Add(informational.Id);
            }

            var active = teamTasks.Where(IsActive).ToList();
            var logs = CalendarItemUrgencyScoreFactory.BuildTeamScoreLogs(active, nowUtc, triggerSource, zoneOf, absencesOf);

            foreach (var task in active.Where(CalendarItemUrgencyScoreFactory.IsRanked))
            {
                var log = logs[task.Id];
                var scoreChanged = Math.Abs(log.FinalScore - task.CurrentUrgencyScore) > LogChangeThreshold;
                if (scoreChanged || task == subject)
                {
                    Stage(context, task, log, nowUtc, changed);
                }
            }

            // A finished subject isn't part of the active graph (it blocks nobody), but an edit still records its score.
            if (subject is not null && subject.TeamId == teamTasks.Key && !IsActive(subject) && CalendarItemUrgencyScoreFactory.IsRanked(subject))
            {
                var log = CalendarItemUrgencyScoreFactory.BuildScoreLog(subject, 0.0, false, nowUtc, triggerSource, zoneOf(subject), absencesOf(subject));
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

    private static bool IsActive(CalendarItem task) => task.Status is not (TaskStatus.Completed or TaskStatus.Cancelled);

    private static void Stage(
        IApplicationDbContext context, CalendarItem task, UrgencyScoreLog log, DateTime nowUtc, List<Guid> changed)
    {
        task.CurrentUrgencyScore = log.FinalScore;
        task.UpdatedAtUtc = nowUtc;
        context.UrgencyScoreLogs.Add(log);
        changed.Add(task.Id);
    }
}
