using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Calendar.Common;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Calendar.Commands.GoogleOAuthCallback;

/// <summary>
/// Second leg of the OAuth2 flow: Google redirects here with a one-time authorization code.
/// This endpoint is <c>[AllowAnonymous]</c> at the Api layer (Google's redirect carries no JWT),
/// so trust is anchored entirely in the unguessable, single-use, TTL'd <c>state</c> value minted
/// by <see cref="PlanFlow.Application.Calendar.Commands.InitiateGoogleConnect.InitiateGoogleConnectCommandHandler"/>.
/// </summary>
public class GoogleOAuthCallbackCommandHandler : IRequestHandler<GoogleOAuthCallbackCommand, CalendarIntegrationDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IGoogleOAuthClient _googleOAuthClient;
    private readonly ICacheService _cacheService;
    private readonly ITokenEncryptionService _tokenEncryptionService;

    public GoogleOAuthCallbackCommandHandler(
        IApplicationDbContext context,
        IGoogleOAuthClient googleOAuthClient,
        ICacheService cacheService,
        ITokenEncryptionService tokenEncryptionService)
    {
        _context = context;
        _googleOAuthClient = googleOAuthClient;
        _cacheService = cacheService;
        _tokenEncryptionService = tokenEncryptionService;
    }

    public async Task<CalendarIntegrationDto> Handle(GoogleOAuthCallbackCommand request, CancellationToken cancellationToken)
    {
        var stateKey = CalendarCacheKeys.OAuthState(request.State);
        var stateEntry = await _cacheService.GetAsync<OAuthStateEntry>(stateKey, cancellationToken)
            ?? throw new UnauthorizedException("OAuth state is missing, expired, or already used.");

        // Single-use: remove immediately so a replayed callback (retry, duplicate redirect) can't
        // reuse the same state to bind a second exchange to the same user.
        await _cacheService.RemoveAsync(stateKey, cancellationToken);

        var tokenResult = await _googleOAuthClient.ExchangeCodeAsync(request.Code, stateEntry.CodeVerifier, cancellationToken);
        var userInfo = await _googleOAuthClient.GetUserInfoAsync(tokenResult.AccessToken, cancellationToken);

        var integration = await _context.CalendarIntegrations
            .FirstOrDefaultAsync(c => c.UserId == stateEntry.UserId && c.Provider == CalendarProvider.Google, cancellationToken);

        if (integration is null)
        {
            integration = new CalendarIntegration
            {
                UserId = stateEntry.UserId,
                Provider = CalendarProvider.Google
            };
            _context.CalendarIntegrations.Add(integration);
        }

        integration.ExternalAccountId = userInfo.Sub;
        integration.ExternalAccountEmail = userInfo.Email;
        integration.EncryptedAccessToken = _tokenEncryptionService.Encrypt(tokenResult.AccessToken);

        // Google omits refresh_token on a re-consent for an already-authorized app unless
        // prompt=consent is forced (see IGoogleOAuthClient's authorization URL) — keep the
        // previously stored one in that case rather than blanking it out.
        if (!string.IsNullOrEmpty(tokenResult.RefreshToken))
        {
            integration.EncryptedRefreshToken = _tokenEncryptionService.Encrypt(tokenResult.RefreshToken);
        }

        integration.AccessTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(tokenResult.ExpiresInSeconds);
        integration.IsActive = true;
        integration.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return new CalendarIntegrationDto(integration.Id, integration.ExternalAccountEmail, integration.LastSyncedAtUtc, integration.IsActive);
    }
}
