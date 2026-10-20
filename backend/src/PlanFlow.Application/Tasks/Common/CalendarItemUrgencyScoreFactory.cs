using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Application.Tasks.Common;

/// <summary>
/// Maps a <see cref="CalendarItem"/>'s raw fields onto the normalized [0,1] inputs
/// <see cref="UrgencyScoreCalculator"/> expects, resolves the dependency chain between tasks
/// and builds the resulting audit log entries.
/// </summary>
public static class CalendarItemUrgencyScoreFactory
{
    /// <summary>Time constant of the deadline curve: the component is exp(-daysRemaining / 7).</summary>
    private const double DeadlineDecayDays = 7.0;

    /// <summary>Same curve for the planned start of a task: exp(-daysUntilStart / 7), and 1.0 once it is time to start.</summary>
    private const double ScheduledDecayDays = 7.0;

    /// <summary>Task without an effort estimate: the "imminent" floor starts 24h before the deadline (the original fixed rule).</summary>
    private const double DefaultTaskFloorWindowHours = 24.0;

    /// <summary>Event without an effort estimate: the floor starts 1h before it begins.</summary>
    private const double DefaultEventFloorWindowHours = 1.0;

    /// <summary>An event with no end time is assumed to last this long (also decides when it counts as over).</summary>
    private const double DefaultEventDurationHours = 1.0;

    /// <summary><see cref="CalendarItem.ImpactScore"/> is authored on a 0–10 scale.</summary>
    private const double MaxImpactScore = 10.0;

    /// <summary>A blocker inherits this share of the urgency of the task it blocks, per step along the chain.</summary>
    private const double InheritanceDecay = 0.9;

    /// <summary>
    /// Whether the item takes part in the ranking. Out of office, Focus time, Working location and Birthday are not decisions
    /// ("shall I do it, and when?"), so scoring them would only add noise: they have no score, are not listed by urgency and
    /// neither inherit nor pass on urgency in a blocking chain. They stay fully visible in the calendar.
    /// </summary>
    public static bool IsRanked(CalendarItem item) => EventTemplateRules.ProfileOf(item) == ScoringProfile.Full;

    /// <summary>
    /// Scores every task in <paramref name="activeTasks"/> (one team's non-finished tasks) at once, because
    /// a task's score depends on its neighbours: a blocker inherits urgency from what it blocks, and a
    /// blocked task is lowered while its blocker is unfinished. Returns one ready-to-persist log per ranked task
    /// (informational items get none). <paramref name="zoneOf"/> gives the time zone in which an all-day item's days are read
    /// (null = UTC). <paramref name="absencesOf"/> gives the out-of-office spans of a task's assignee (null = none known): they are taken
    /// off the task's deadline (see <see cref="AbsenceDeadline"/>).
    /// </summary>
    public static IReadOnlyDictionary<Guid, UrgencyScoreLog> BuildTeamScoreLogs(
        IReadOnlyCollection<CalendarItem> activeTasks, DateTime nowUtc, ScoreTriggerSource triggerSource,
        Func<CalendarItem, TimeZoneInfo>? zoneOf = null, Func<CalendarItem, IReadOnlyList<AbsenceSpan>?>? absencesOf = null)
    {
        zoneOf ??= _ => TimeZoneInfo.Utc;
        absencesOf ??= _ => null;
        var ranked = activeTasks.Where(IsRanked).ToList();
        var byId = ranked.ToDictionary(t => t.Id);

        // A blocker only counts while it is unfinished and not a past event (a finished meeting no longer holds anything up).
        bool StillBlocks(Guid blockerId) =>
            byId.TryGetValue(blockerId, out var blocker) && !IsPastEvent(blocker, nowUtc, zoneOf(blocker));

        var blockedBy = ranked
            .Where(t => t.BlockedByTaskId is { } blockerId && StillBlocks(blockerId))
            .ToLookup(t => t.BlockedByTaskId!.Value);

        var blocking = new Dictionary<Guid, double>();
        var effective = new Dictionary<Guid, double>();
        var visiting = new HashSet<Guid>();

        // Effective (unpenalized) score of a task; its blocking component is 0.9 x the best effective
        // score among the tasks it blocks, which recursively covers the whole chain. The `visiting` set
        // only guards against a corrupted cycle (the API rejects cycles) so this can never loop forever.
        double Effective(CalendarItem task)
        {
            if (effective.TryGetValue(task.Id, out var cached))
            {
                return cached;
            }
            if (!visiting.Add(task.Id))
            {
                return 0.0;
            }

            var inherited = 0.0;
            foreach (var downstream in blockedBy[task.Id])
            {
                inherited = Math.Max(inherited, InheritanceDecay * Effective(downstream));
            }

            visiting.Remove(task.Id);
            blocking[task.Id] = inherited;
            return effective[task.Id] = Breakdown(task, inherited, nowUtc, zoneOf(task), absencesOf(task)).EffectiveScore;
        }

        var logs = new Dictionary<Guid, UrgencyScoreLog>(ranked.Count);
        foreach (var task in ranked)
        {
            Effective(task);
            var isBlocked = task.BlockedByTaskId is { } blockerId && StillBlocks(blockerId);
            logs[task.Id] = BuildScoreLog(
                task, blocking.GetValueOrDefault(task.Id), isBlocked, nowUtc, triggerSource, zoneOf(task), absencesOf(task));
        }

        return logs;
    }

    /// <summary>
    /// Builds the audit log for one task when its blocking component and blocked state are already known.
    /// Prefer <see cref="BuildTeamScoreLogs"/> in production code, which resolves those from the dependency chain.
    /// </summary>
    public static UrgencyScoreLog BuildScoreLog(
        CalendarItem task, double blockingComponent, bool isBlocked, DateTime nowUtc,
        ScoreTriggerSource triggerSource = ScoreTriggerSource.ManualUpdate, TimeZoneInfo? zone = null,
        IReadOnlyList<AbsenceSpan>? absences = null)
    {
        zone ??= TimeZoneInfo.Utc;
        var breakdown = Breakdown(task, blockingComponent, nowUtc, zone, absences);
        var finalScore = UrgencyScoreCalculator.ApplyBlockedPenalty(breakdown.EffectiveScore, isBlocked);

        return new UrgencyScoreLog
        {
            CalendarItemId = task.Id,
            DeadlineComponent = breakdown.Deadline,
            BlockingComponent = breakdown.Blocking,
            ImpactComponent = breakdown.Impact,
            UserOverrideComponent = breakdown.UserOverride,
            ScheduledComponent = breakdown.Scheduled,
            AlgorithmVersion = UrgencyScoreCalculator.AlgorithmVersion,
            IsBlocked = isBlocked,
            FinalScore = finalScore,
            TriggerSource = triggerSource,
            ReasonText = BuildReasonText(task, breakdown, isBlocked, finalScore, nowUtc, zone, absences)
        };
    }

    /// <summary>Priority label shown in front of the reason; the thresholds are the same as the mobile app's urgency tiers.</summary>
    public static string PriorityLabel(double score) => score switch
    {
        >= 0.8 => "[Critical]",
        >= 0.6 => "[High]",
        >= 0.4 => "[Medium]",
        _ => "[Low]"
    };

    // One-line, fully deterministic explanation: "[Priority] timing, then the other factors that shaped the score".
    // No randomness and no clock reads (only the nowUtc passed in), so the same input always gives the same text.
    private static string BuildReasonText(
        CalendarItem task, ScoreBreakdown breakdown, bool isBlocked, double finalScore, DateTime nowUtc, TimeZoneInfo zone,
        IReadOnlyList<AbsenceSpan>? absences)
    {
        if (!IsRanked(task))
        {
            return "Informational item - shown in the calendar, not ranked";
        }

        var label = PriorityLabel(finalScore);

        if (IsPastEvent(task, nowUtc, zone))
        {
            var end = ResolveEventEnd(task, zone)!.Value;
            return $"{label} Event ended {FormatSpan(nowUtc - end)} ago - history, no priority";
        }

        var parts = new List<string> { DescribeTiming(task, nowUtc, ResolveTiming(task, zone, nowUtc, absences)) };

        if (DescribePlannedStart(task, breakdown, nowUtc, zone) is { } plannedStart)
        {
            parts.Add(plannedStart);
        }
        if (breakdown.Blocking > 0.0)
        {
            parts.Add($"holds up urgent work (inherits {(int)Math.Round(breakdown.Blocking * 100)}%)");
        }
        if (isBlocked)
        {
            parts.Add("blocked by an unfinished task (score halved)");
        }
        parts.Add(task.ImpactScore is { } impact ? $"impact {impact}/10" : "impact not rated");
        if (breakdown.UserOverride > 0.0)
        {
            parts.Add("manual boost active");
        }
        if (task.IsDataIncomplete)
        {
            parts.Add("details not confirmed yet");
        }

        return $"{label} {string.Join(", ", parts)}";
    }

    private static string DescribeTiming(CalendarItem task, DateTime nowUtc, Timing timing)
    {
        if (task.TaskType == TaskType.Event)
        {
            if (timing.TargetUtc is not { } start)
            {
                return "no start time";
            }
            if (start <= nowUtc)
            {
                return $"in progress, started {FormatSpan(nowUtc - start)} ago";
            }

            var prep = task.EstimatedEffortHours ?? 0.0;
            return prep > 0.0 && timing.EffectiveDeadlineUtc <= nowUtc
                ? $"not enough time: needs {FormatSpan(TimeSpan.FromHours(prep))} of effort, starts in {FormatSpan(start - nowUtc)}"
                : $"starts in {FormatSpan(start - nowUtc)}";
        }

        if (timing.TargetUtc is not { } deadline)
        {
            return "no deadline";
        }
        if (deadline <= nowUtc)
        {
            return $"overdue by {FormatSpan(nowUtc - deadline)}";
        }

        var effort = task.EstimatedEffortHours ?? 0.0;
        var absenceNote = timing.Absence > TimeSpan.Zero ? $" ({FormatDays(timing.Absence)} out of office)" : string.Empty;
        if ((effort > 0.0 || timing.Absence > TimeSpan.Zero) && timing.EffectiveDeadlineUtc <= nowUtc)
        {
            return effort > 0.0
                ? $"not enough time: needs {FormatSpan(TimeSpan.FromHours(effort))}, only {FormatSpan(deadline - nowUtc)} left{absenceNote}"
                : $"not enough time: only {FormatSpan(deadline - nowUtc)} left{absenceNote}";
        }

        return $"due in {FormatSpan(deadline - nowUtc)}{absenceNote}";
    }

    // "2 d", "1.5 d" or, below a day, "6h": how much out-of-office time was taken off the deadline.
    private static string FormatDays(TimeSpan span) =>
        span.TotalDays >= 1.0 ? $"{Math.Round(span.TotalDays, 1):0.#} d" : $"{Math.Max(1, (int)Math.Round(span.TotalHours))}h";

    // Only said when the planned start actually counted toward the score (a task still in Backlog with a confirmed start).
    private static string? DescribePlannedStart(CalendarItem task, ScoreBreakdown breakdown, DateTime nowUtc, TimeZoneInfo zone)
    {
        if (breakdown.Scheduled is null || Moment(task.StartUtc, task.IsAllDay, zone) is not { } begins)
        {
            return null;
        }
        if (begins > nowUtc)
        {
            return $"planned start in {FormatSpan(begins - nowUtc)}";
        }

        // A day-long start is not late until that whole day is over ("today" is still on time).
        var startDayRunning = task.IsAllDay && Moment(task.StartUtc!.Value.AddDays(1), true, zone) is { } dayEnd && dayEnd > nowUtc;
        return startDayRunning ? "planned start today" : $"not started, planned start was {FormatSpan(nowUtc - begins)} ago";
    }

    // Rounds down to the largest sensible unit: minutes below 1h, hours below 48h, days above.
    private static string FormatSpan(TimeSpan span)
    {
        var abs = span.Duration();
        if (abs.TotalHours < 1.0)
        {
            return $"{Math.Max(1, (int)abs.TotalMinutes)} min";
        }
        return abs.TotalHours < 48.0 ? $"{(int)abs.TotalHours}h" : $"{(int)abs.TotalDays}d";
    }

    private readonly record struct ScoreBreakdown(
        double Deadline, double Blocking, double? Impact, double UserOverride, double? Scheduled, double EffectiveScore);

    private static ScoreBreakdown Breakdown(
        CalendarItem task, double blockingComponent, DateTime nowUtc, TimeZoneInfo zone, IReadOnlyList<AbsenceSpan>? absences)
    {
        // An informational item has no score at all, and a finished event is history, not a decision: it stops generating
        // priority altogether (no alert fatigue from meetings that already happened) and stays in the calendar only as a record.
        if (!IsRanked(task) || IsPastEvent(task, nowUtc, zone))
        {
            return new ScoreBreakdown(0.0, 0.0, null, 0.0, null, 0.0);
        }

        var timing = ResolveTiming(task, zone, nowUtc, absences);
        var deadline = ComputeDeadlineComponent(timing.EffectiveDeadlineUtc, nowUtc);
        var blocking = Math.Clamp(blockingComponent, 0.0, 1.0);
        double? impact = task.ImpactScore is { } rating ? Math.Clamp(rating / MaxImpactScore, 0.0, 1.0) : null;
        var userOverride = ComputeOverrideComponent(task, nowUtc);
        var scheduled = ComputeScheduledComponent(task, nowUtc, zone);

        // An unverified item (imported without impact / effort, so its numbers are placeholders) never gets the "imminent" floor:
        // an old task from Google would otherwise jump straight to Critical and trigger alerts before anyone has looked at it.
        var imminent = !task.IsDataIncomplete && timing.TargetUtc is { } target && (target - nowUtc).TotalHours <= timing.FloorWindowHours;

        var effective = UrgencyScoreCalculator.Calculate(deadline, blocking, impact, userOverride, imminent, scheduled);
        return new ScoreBreakdown(deadline, blocking, impact, userOverride, scheduled, effective);
    }

    /// <param name="TargetUtc">The moment that matters: a Task's deadline, an Event's start.</param>
    /// <param name="EffectiveDeadlineUtc">When the clock really runs out: the work must START by the target minus its effort (a Task's deadline, an Event's start).</param>
    /// <param name="FloorWindowHours">How long before <paramref name="TargetUtc"/> the item counts as "imminent" and gets the score floor.</param>
    /// <param name="Absence">Out-of-office time of the assignee between now and the deadline that was taken off <paramref name="EffectiveDeadlineUtc"/>.</param>
    private readonly record struct Timing(DateTime? TargetUtc, DateTime? EffectiveDeadlineUtc, double FloorWindowHours, TimeSpan Absence = default);

    // Task:  effectiveDeadline = deadline - effort - the assignee's absence before it, so a 6h job due in 5h is already "overdue" (it cannot
    //        be finished anymore) and so is a job due Friday by someone who is away from Wednesday.
    //        The floor window is the effort itself (24h when no estimate, which equals the original fixed rule).
    //        A day-long deadline (a Google task is due on a day, not at a time) runs out at the END of that day in the person's zone.
    // Event: happens at its start (the beginning of the first day when all-day). Its effort is the work that has to be done before it
    //        (preparing the talk, travelling there), so the same rule as for a task applies: effectiveDeadline = start - effort, and the
    //        floor window is the effort itself (1h when no estimate). Once its end has passed the score is 0 (see Breakdown).
    private static Timing ResolveTiming(CalendarItem task, TimeZoneInfo zone, DateTime nowUtc, IReadOnlyList<AbsenceSpan>? absences)
    {
        if (task.TaskType == TaskType.Event)
        {
            var start = Moment(task.StartUtc, task.IsAllDay, zone);
            var prep = Math.Max(task.EstimatedEffortHours ?? 0.0, 0.0);
            return new Timing(start, start?.AddHours(-prep), task.EstimatedEffortHours ?? DefaultEventFloorWindowHours);
        }

        var effort = Math.Max(task.EstimatedEffortHours ?? 0.0, 0.0);
        var deadline = task.IsAllDay && task.DeadlineUtc is { } day ? Moment(day.AddDays(1), true, zone) : task.DeadlineUtc;
        var absence = deadline is { } due ? AbsenceDeadline.Deduction(absences, nowUtc, due) : TimeSpan.Zero;
        return new Timing(
            deadline,
            deadline?.AddHours(-effort) - absence,
            task.EstimatedEffortHours ?? DefaultTaskFloorWindowHours,
            absence);
    }

    // An all-day item keeps its day as a literal date (midnight UTC); the real moment that day begins depends on the person's zone.
    private static DateTime? Moment(DateTime? literal, bool allDay, TimeZoneInfo zone) =>
        literal is { } value && allDay ? AllDayTime.StartOfLocalDayUtc(value, zone) : literal;

    /// <summary>When an event ends: its EndUtc, or one default-duration after the start when no end was given.</summary>
    public static DateTime? ResolveEventEnd(DateTime? startUtc, DateTime? endUtc) =>
        endUtc ?? startUtc?.AddHours(DefaultEventDurationHours);

    /// <summary>
    /// When an event ends for a person in <paramref name="zone"/> (null = UTC). An all-day event ends when its last day does:
    /// its stored end is the midnight AFTER that day, which is read as a day, not as an instant in UTC.
    /// </summary>
    public static DateTime? ResolveEventEnd(CalendarItem item, TimeZoneInfo? zone = null)
    {
        if (!item.IsAllDay)
        {
            return ResolveEventEnd(item.StartUtc, item.EndUtc);
        }

        zone ??= TimeZoneInfo.Utc;
        return item.EndUtc is { } end ? Moment(end, true, zone)
            : item.StartUtc is { } start ? Moment(start.AddDays(1), true, zone)
            : null;
    }

    /// <summary>True for an Event whose end has passed. A Task never counts as past - an overdue Task stays at maximum urgency.</summary>
    public static bool IsPastEvent(CalendarItem item, DateTime nowUtc, TimeZoneInfo? zone = null) =>
        item.TaskType == TaskType.Event && ResolveEventEnd(item, zone) is { } end && end < nowUtc;

    // Exponential curve: 1.0 when due/overdue, ~0.87 a day before, ~0.65 at 3 days, ~0.37 at a week,
    // ~0.01 at a month. It rises fastest exactly where the decision gets urgent (the last days).
    private static double ComputeDeadlineComponent(DateTime? deadlineUtc, DateTime nowUtc)
    {
        if (deadlineUtc is null)
        {
            return 0.0;
        }

        var daysRemaining = (deadlineUtc.Value - nowUtc).TotalDays;
        if (daysRemaining <= 0)
        {
            return 1.0;
        }

        return Math.Exp(-daysRemaining / DeadlineDecayDays);
    }

    // Planned start of a task (events have none): the same curve as the deadline, reaching 1.0 when it is time to start. It counts
    // only for a task still waiting in Backlog (once started it has done its job), and not for an unverified item - the start of an
    // imported task is just the day it was synced, which would give every import the same bonus. null = the component is left out.
    private static double? ComputeScheduledComponent(CalendarItem task, DateTime nowUtc, TimeZoneInfo zone)
    {
        if (task.TaskType != TaskType.Task || task.Status != TaskStatus.Backlog || task.IsDataIncomplete
            || Moment(task.StartUtc, task.IsAllDay, zone) is not { } begins)
        {
            return null;
        }

        var daysToStart = (begins - nowUtc).TotalDays;
        return daysToStart <= 0 ? 1.0 : Math.Exp(-daysToStart / ScheduledDecayDays);
    }

    private static double ComputeOverrideComponent(CalendarItem task, DateTime nowUtc)
    {
        if (task.ManualUrgencyOverride is not { } overrideValue)
        {
            return 0.0;
        }

        if (task.ManualUrgencyOverrideExpiresAtUtc is { } expiresAtUtc && expiresAtUtc <= nowUtc)
        {
            return 0.0;
        }

        return Math.Clamp(overrideValue, 0.0, 1.0);
    }
}
