using Moq;
using PlanFlow.Application.Calendar.Commands.InitiateGoogleConnect;
using PlanFlow.Application.Calendar.Common;
using PlanFlow.Application.Common.Interfaces;
using Xunit;

namespace PlanFlow.Tests.Application.Calendar;

public class InitiateGoogleConnectCommandHandlerTests
{
    [Fact]
    public async Task Handle_StoresStateInCacheAndReturnsGoogleAuthorizationUrl()
    {
        var googleClient = new Mock<IGoogleOAuthClient>();
        googleClient
            .Setup(c => c.BuildAuthorizationUrl(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("https://accounts.google.com/o/oauth2/v2/auth?client_id=fake");

        var cache = new Mock<ICacheService>();
        OAuthStateEntry? storedEntry = null;
        cache
            .Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<OAuthStateEntry>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<string, OAuthStateEntry, TimeSpan, CancellationToken>((_, entry, _, _) => storedEntry = entry)
            .Returns(Task.CompletedTask);

        var handler = new InitiateGoogleConnectCommandHandler(googleClient.Object, cache.Object);
        var userId = Guid.NewGuid();

        var result = await handler.Handle(new InitiateGoogleConnectCommand(userId), CancellationToken.None);

        Assert.Equal("https://accounts.google.com/o/oauth2/v2/auth?client_id=fake", result.AuthorizationUrl);
        Assert.NotNull(storedEntry);
        Assert.Equal(userId, storedEntry!.UserId);
        Assert.False(string.IsNullOrWhiteSpace(storedEntry.CodeVerifier));
    }

    [Fact]
    public async Task Handle_PassesAPkceChallengeDerivedFromTheStoredVerifier()
    {
        var googleClient = new Mock<IGoogleOAuthClient>();
        string? capturedChallenge = null;
        googleClient
            .Setup(c => c.BuildAuthorizationUrl(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((_, challenge) => capturedChallenge = challenge)
            .Returns("https://accounts.google.com/authorize");

        var cache = new Mock<ICacheService>();
        OAuthStateEntry? storedEntry = null;
        cache
            .Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<OAuthStateEntry>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<string, OAuthStateEntry, TimeSpan, CancellationToken>((_, entry, _, _) => storedEntry = entry)
            .Returns(Task.CompletedTask);

        var handler = new InitiateGoogleConnectCommandHandler(googleClient.Object, cache.Object);

        await handler.Handle(new InitiateGoogleConnectCommand(Guid.NewGuid()), CancellationToken.None);

        var expectedChallenge = PlanFlow.Application.Common.Security.PkceGenerator.DeriveCodeChallenge(storedEntry!.CodeVerifier);
        Assert.Equal(expectedChallenge, capturedChallenge);
    }
}
