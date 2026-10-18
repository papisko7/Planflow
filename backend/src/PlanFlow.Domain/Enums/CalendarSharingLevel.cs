namespace PlanFlow.Domain.Enums;

/// <summary>How much of a user's personal calendar their teammates may see. Trimmed on the server, never on the client.</summary>
public enum CalendarSharingLevel
{
    /// <summary>Teammates see only team items; the member's availability shows as "no data".</summary>
    Off = 0,

    /// <summary>Teammates see template types and busy blocks, without titles, descriptions or locations (default).</summary>
    Availability = 1,

    /// <summary>Teammates also see titles, times and locations (events marked private in Google stay "Busy").</summary>
    Details = 2
}
