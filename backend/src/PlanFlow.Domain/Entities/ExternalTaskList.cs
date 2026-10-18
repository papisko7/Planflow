using PlanFlow.Domain.Common;

namespace PlanFlow.Domain.Entities;

/// <summary>
/// One Google Tasks list (e.g. "My Tasks", "Shopping") of a connected account. Tasks imported from it point back here, so the
/// app can show them grouped under the list's name. Read-only mirror: PlanFlow never writes to Google.
/// </summary>
public class ExternalTaskList : BaseEntity
{
    public Guid CalendarIntegrationId { get; set; }
    public CalendarIntegration CalendarIntegration { get; set; } = null!;

    /// <summary>Google's id of the list; together with the integration it identifies the row (the sync upserts on it).</summary>
    public string ExternalId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
}
