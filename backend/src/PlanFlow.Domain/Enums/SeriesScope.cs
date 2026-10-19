namespace PlanFlow.Domain.Enums;

/// <summary>Which occurrences of a repeating item an edit or a delete applies to (the "This / This and following / All" choice). Plain int on the wire.</summary>
public enum SeriesScope
{
    /// <summary>Only the occurrence that was opened. The default, and the only meaning for an item that does not repeat.</summary>
    This = 0,

    /// <summary>The opened occurrence and every later one. The series is cut in two: the earlier part keeps its old rule.</summary>
    ThisAndFollowing = 1,

    /// <summary>Every occurrence, past and future.</summary>
    All = 2
}
