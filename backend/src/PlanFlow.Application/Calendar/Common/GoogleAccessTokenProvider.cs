using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Calendar.Common;

/// <summary>
/// Returns a live Google access token for a stored <see cref="CalendarIntegration"/>, transparently
/// refreshing it first when expired. Used by anything that needs to call the Google Calendar API on
/// a user's behalf (the Phase 4A sync job); callers never see a persisted plaintext token — this is
/// the only place ciphertext is decrypted, and a refreshed token is re-encrypted before being saved.
/// </summary>
public class GoogleAccessTokenProvider
{
    // Refresh a little ahead of the real expiry so a token doesn't die mid-request.
    private static readonly TimeSpan ExpiryBuffer = TimeSpan.FromMinutes(2);

    private readonly IGoogleOAuthClient _googleOAuthClient;
    private readonly ITokenEncryptionService _tokenEncryptionService;
    private readonly IApplicationDbContext _context;

    public GoogleAccessTokenProvider(
        IGoogleOAuthClient googleOAuthClient,
        ITokenEncryptionService tokenEncryptionService,
        IApplicationDbContext context)
    {
        _googleOAuthClient = googleOAuthClient;
        _tokenEncryptionService = tokenEncryptionService;
        _context = context;
    }

    public async Task<string> GetValidAccessTokenAsync(CalendarIntegration integration, CancellationToken cancellationToken)
    {
        if (integration.AccessTokenExpiresAtUtc - ExpiryBuffer > DateTime.UtcNow)
        {
            return _tokenEncryptionService.Decrypt(integration.EncryptedAccessToken);
        }

        return await RefreshAccessTokenAsync(integration, cancellationToken);
    }

    /// <summary>
    /// Unconditionally exchanges the stored refresh token for a new access token, bypassing the
    /// local expiry check. Used when Google rejects a request with a 401 even though our locally
    /// stored expiry says the token should still be valid (e.g. the user revoked and re-granted
    /// consent) — see <see cref="PlanFlow.Infrastructure.BackgroundJobs.SyncCalendarJob"/>.
    /// </summary>
    /// <summary>
    /// Runs one Google call with a valid token. If Google answers 401 although our stored expiry said the token was fine (consent revoked
    /// and re-granted out of band), the token is refreshed and the call repeated exactly once; a second 401 propagates to the caller.
    /// </summary>
    public async Task<T> CallAsync<T>(CalendarIntegration integration, Func<string, Task<T>> call, CancellationToken cancellationToken)
    {
        var accessToken = await GetValidAccessTokenAsync(integration, cancellationToken);
        try
        {
            return await call(accessToken);
        }
        catch (UnauthorizedException)
        {
            return await call(await RefreshAccessTokenAsync(integration, cancellationToken));
        }
    }

    public async Task<string> RefreshAccessTokenAsync(CalendarIntegration integration, CancellationToken cancellationToken)
    {
        var refreshToken = _tokenEncryptionService.Decrypt(integration.EncryptedRefreshToken);
        var refreshed = await _googleOAuthClient.RefreshAccessTokenAsync(refreshToken, cancellationToken);

        integration.EncryptedAccessToken = _tokenEncryptionService.Encrypt(refreshed.AccessToken);
        integration.AccessTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(refreshed.ExpiresInSeconds);
        // Google lists the scopes the grant still covers; keeping them current is what tells the app whether Tasks may be read.
        if (!string.IsNullOrWhiteSpace(refreshed.Scope))
        {
            integration.GrantedScopes = refreshed.Scope;
        }
        integration.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return refreshed.AccessToken;
    }
}
