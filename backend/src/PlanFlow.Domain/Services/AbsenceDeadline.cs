namespace PlanFlow.Domain.Services;

/// <summary>A stretch of real time (UTC instants) in which a person is out of office.</summary>
public readonly record struct AbsenceSpan(DateTime StartUtc, DateTime EndUtc);

/// <summary>
/// Out-of-office time that eats into a task's deadline: a task due Friday whose assignee is away Wednesday to Friday really has to be done
/// before Wednesday. Pure functions, no database. The deduction is capped, so a months-long leave cannot push every task of that person
/// straight to the top of the ranking.
/// </summary>
public static class AbsenceDeadline
{
    /// <summary>The most absence time that is taken off a deadline.</summary>
    public static readonly TimeSpan MaxDeduction = TimeSpan.FromDays(7);

    /// <summary>
    /// The absence between <paramref name="fromUtc"/> and <paramref name="toUtc"/>, overlapping spans counted once and capped at
    /// <see cref="MaxDeduction"/>. Zero when there is none (or the window is empty).
    /// </summary>
    public static TimeSpan Deduction(IEnumerable<AbsenceSpan>? spans, DateTime fromUtc, DateTime toUtc)
    {
        if (spans is null || toUtc <= fromUtc)
        {
            return TimeSpan.Zero;
        }

        var total = TimeSpan.Zero;
        DateTime? currentStart = null;
        DateTime currentEnd = default;
        foreach (var span in spans
            .Select(s => (Start: s.StartUtc > fromUtc ? s.StartUtc : fromUtc, End: s.EndUtc < toUtc ? s.EndUtc : toUtc))
            .Where(s => s.End > s.Start)
            .OrderBy(s => s.Start))
        {
            if (currentStart is null)
            {
                currentStart = span.Start;
                currentEnd = span.End;
            }
            else if (span.Start <= currentEnd)
            {
                currentEnd = span.End > currentEnd ? span.End : currentEnd;
            }
            else
            {
                total += currentEnd - currentStart.Value;
                currentStart = span.Start;
                currentEnd = span.End;
            }
        }

        if (currentStart is not null)
        {
            total += currentEnd - currentStart.Value;
        }

        return total > MaxDeduction ? MaxDeduction : total;
    }

    /// <summary>True when <paramref name="atUtc"/> falls inside one of the spans (the end is exclusive).</summary>
    public static bool Covers(IEnumerable<AbsenceSpan>? spans, DateTime atUtc) =>
        spans is not null && spans.Any(s => atUtc >= s.StartUtc && atUtc < s.EndUtc);
}
