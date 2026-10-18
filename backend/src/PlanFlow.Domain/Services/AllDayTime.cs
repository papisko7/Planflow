namespace PlanFlow.Domain.Services;

/// <summary>
/// An all-day item stores its day as midnight UTC "read literally" (20 Oct is always 20 Oct, wherever the person is). The
/// algorithm needs real moments, so this turns such a day into the moment it begins for a person in their own time zone.
/// </summary>
public static class AllDayTime
{
    /// <summary>The moment the calendar day <paramref name="literalDay"/> (its date part is read literally) begins in <paramref name="zone"/>.</summary>
    public static DateTime StartOfLocalDayUtc(DateTime literalDay, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(literalDay.Date, DateTimeKind.Unspecified);

        // In a few zones the clock jumps over midnight on a daylight-saving day; the day then begins at the first moment that exists.
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(local, zone), DateTimeKind.Utc);
    }

    /// <summary>The user's zone, or UTC when none is known or the name is not recognised on this machine.</summary>
    public static TimeZoneInfo ZoneOrUtc(string? timeZoneId) =>
        !string.IsNullOrWhiteSpace(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone)
            ? zone
            : TimeZoneInfo.Utc;
}
