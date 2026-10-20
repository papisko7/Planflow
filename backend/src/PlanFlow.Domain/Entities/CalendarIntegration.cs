using PlanFlow.Domain.Common;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Domain.Entities;

/// <summary>
/// A user's linked external calendar (Google OAuth2). Tokens must be encrypted at rest
/// by the Infrastructure layer; the Domain only models the shape of the data.
/// </summary>
public class CalendarIntegration : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public CalendarProvider Provider { get; set; } = CalendarProvider.Google;

    /// <summary>Stable id of the external account/calendar (e.g. Google's account id), used as the sync idempotency key.</summary>
    public string ExternalAccountId { get; set; } = string.Empty;
    public string ExternalAccountEmail { get; set; } = string.Empty;
    public string EncryptedAccessToken { get; set; } = string.Empty;
    public string EncryptedRefreshToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresAtUtc { get; set; }

    /// <summary>When the last poll started. The next incremental poll asks Google only for events changed since then.</summary>
    public DateTime? LastSyncedAtUtc { get; set; }

    /// <summary>
    /// When the whole forward window (now to +90 days) was last fetched. Incremental polls cannot see events that merely
    /// <i>enter</i> the window as time passes, so a full fetch is repeated once a day (and covers anything a poll missed).
    /// </summary>
    public DateTime? LastFullSyncAtUtc { get; set; }

    /// <summary>When the Google Tasks job last started a poll for this account. The next poll asks only for tasks changed since then; null = nothing imported yet.</summary>
    public DateTime? LastTasksSyncedAtUtc { get; set; }

    /// <summary>Space-separated OAuth scopes Google reported at the last consent/refresh. An account connected before Google Tasks existed lacks the tasks scope, and one connected before changes could be sent back lacks the write scopes; both must reconnect.</summary>
    public string GrantedScopes { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>Start shared by both Tasks scopes ("tasks" and "tasks.readonly"): a stored scope list containing it grants some access to Google Tasks.</summary>
    public const string TasksScopeBase = "https://www.googleapis.com/auth/tasks";

    /// <summary>Read and change Google Tasks.</summary>
    public const string TasksWriteScope = TasksScopeBase;

    /// <summary>Read Google Tasks only (what accounts connected before changes could be sent back have).</summary>
    public const string TasksScope = TasksScopeBase + ".readonly";

    /// <summary>Read and change events of the user's calendars.</summary>
    public const string CalendarWriteScope = "https://www.googleapis.com/auth/calendar.events";

    private const string CalendarFullScope = "https://www.googleapis.com/auth/calendar";

    private bool Has(string scope) => GrantedScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope);

    /// <summary>True when the user agreed (on the consent screen) to let PlanFlow read their Google Tasks.</summary>
    public bool HasTasksScope => Has(TasksScope) || Has(TasksWriteScope);

    /// <summary>True when PlanFlow may change the user's Google Tasks, so an edit made in the app can be sent back.</summary>
    public bool CanWriteTasks => Has(TasksWriteScope);

    /// <summary>True when PlanFlow may change the user's calendar events, so an edit made in the app can be sent back.</summary>
    public bool CanWriteCalendar => Has(CalendarWriteScope) || Has(CalendarFullScope);
}
