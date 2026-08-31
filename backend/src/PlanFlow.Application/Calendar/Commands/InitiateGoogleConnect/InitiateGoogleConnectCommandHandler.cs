using MediatR;
using PlanFlow.Application.Calendar.Common;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Common.Security;

namespace PlanFlow.Application.Calendar.Commands.InitiateGoogleConnect;

public class InitiateGoogleConnectCommandHandler : IRequestHandler<InitiateGoogleConnectCommand, InitiateGoogleConnectResult>
{
    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);

    private readonly IGoogleOAuthClient _googleOAuthClient;
    private readonly ICacheService _cacheService;

    public InitiateGoogleConnectCommandHandler(IGoogleOAuthClient googleOAuthClient, ICacheService cacheService)
    {
        _googleOAuthClient = googleOAuthClient;
        _cacheService = cacheService;
    }

    public async Task<InitiateGoogleConnectResult> Handle(InitiateGoogleConnectCommand request, CancellationToken cancellationToken)
    {
        var state = PkceGenerator.GenerateState();
        var codeVerifier = PkceGenerator.GenerateCodeVerifier();
        var codeChallenge = PkceGenerator.DeriveCodeChallenge(codeVerifier);

        await _cacheService.SetAsync(
            CalendarCacheKeys.OAuthState(state),
            new OAuthStateEntry(request.UserId, codeVerifier),
            StateTtl,
            cancellationToken);

        var authorizationUrl = _googleOAuthClient.BuildAuthorizationUrl(state, codeChallenge);
        return new InitiateGoogleConnectResult(authorizationUrl);
    }
}
