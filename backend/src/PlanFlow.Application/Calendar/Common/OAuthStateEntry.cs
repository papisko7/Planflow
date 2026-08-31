namespace PlanFlow.Application.Calendar.Common;

/// <summary>
/// Cached (Redis, ~10 min TTL) under key <c>oauth:google:state:{state}</c> between the
/// /connect and /callback legs of the flow. Binds the unguessable <c>state</c> value back to
/// the user who started the flow and the PKCE verifier only they (via this server) know,
/// so the callback endpoint — hit by Google's redirect, not the authenticated mobile client —
/// can still be trusted despite being <c>[AllowAnonymous]</c>.
/// </summary>
public record OAuthStateEntry(Guid UserId, string CodeVerifier);

public record CalendarIntegrationDto(
    Guid Id,
    string ExternalAccountEmail,
    DateTime? LastSyncedAtUtc,
    bool IsActive);
