using Microsoft.EntityFrameworkCore;
using Moq;
using PlanFlow.Application.Calendar.Commands.GoogleOAuthCallback;
using PlanFlow.Application.Calendar.Common;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;
using Xunit;

namespace PlanFlow.Tests.Application.Calendar;

public class GoogleOAuthCallbackCommandHandlerTests
{
    private static PlanFlowDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<PlanFlowDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new PlanFlowDbContext(options);
    }

    private static (Mock<IGoogleOAuthClient> GoogleClient, Mock<ICacheService> Cache, Mock<ITokenEncryptionService> Encryption) CreateMocks(
        OAuthStateEntry? cachedState)
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<OAuthStateEntry>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedState);
        cache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var googleClient = new Mock<IGoogleOAuthClient>();
        googleClient.Setup(c => c.ExchangeCodeAsync("valid-code", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleTokenResult("access-token-raw", "refresh-token-raw", 3600, "openid email"));
        googleClient.Setup(c => c.GetUserInfoAsync("access-token-raw", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleUserInfo("google-sub-123", "student@example.com"));

        var encryption = new Mock<ITokenEncryptionService>();
        encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns<string>(s => $"enc({s})");

        return (googleClient, cache, encryption);
    }

    [Fact]
    public async Task Handle_ValidStateAndCode_CreatesAnActiveEncryptedCalendarIntegration()
    {
        var context = CreateInMemoryContext();
        var userId = Guid.NewGuid();
        var (googleClient, cache, encryption) = CreateMocks(new OAuthStateEntry(userId, "verifier-abc"));
        var handler = new GoogleOAuthCallbackCommandHandler(context, googleClient.Object, cache.Object, encryption.Object);

        var result = await handler.Handle(new GoogleOAuthCallbackCommand("valid-code", "state-xyz"), CancellationToken.None);

        Assert.True(result.IsActive);
        Assert.Equal("student@example.com", result.ExternalAccountEmail);

        var stored = await context.CalendarIntegrations.SingleAsync();
        Assert.Equal(userId, stored.UserId);
        Assert.Equal(CalendarProvider.Google, stored.Provider);
        Assert.Equal("google-sub-123", stored.ExternalAccountId);
        Assert.Equal("enc(access-token-raw)", stored.EncryptedAccessToken);
        Assert.Equal("enc(refresh-token-raw)", stored.EncryptedRefreshToken);
        Assert.NotEqual("access-token-raw", stored.EncryptedAccessToken);
    }

    [Fact]
    public async Task Handle_MissingOrExpiredState_ThrowsUnauthorizedAndNeverCallsGoogle()
    {
        var context = CreateInMemoryContext();
        var (googleClient, cache, encryption) = CreateMocks(cachedState: null);
        var handler = new GoogleOAuthCallbackCommandHandler(context, googleClient.Object, cache.Object, encryption.Object);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => handler.Handle(new GoogleOAuthCallbackCommand("some-code", "unknown-state"), CancellationToken.None));

        googleClient.Verify(c => c.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ValidState_RemovesItFromCacheSoItCannotBeReplayed()
    {
        var context = CreateInMemoryContext();
        var userId = Guid.NewGuid();
        var (googleClient, cache, encryption) = CreateMocks(new OAuthStateEntry(userId, "verifier-abc"));
        var handler = new GoogleOAuthCallbackCommandHandler(context, googleClient.Object, cache.Object, encryption.Object);

        await handler.Handle(new GoogleOAuthCallbackCommand("valid-code", "state-xyz"), CancellationToken.None);

        cache.Verify(c => c.RemoveAsync(CalendarCacheKeys.OAuthState("state-xyz"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReconnectingAnExistingIntegration_UpdatesTheSameRowInsteadOfDuplicating()
    {
        var context = CreateInMemoryContext();
        var userId = Guid.NewGuid();
        context.CalendarIntegrations.Add(new CalendarIntegration
        {
            UserId = userId,
            Provider = CalendarProvider.Google,
            ExternalAccountId = "old-sub",
            ExternalAccountEmail = "old@example.com",
            EncryptedAccessToken = "enc(old-access)",
            EncryptedRefreshToken = "enc(old-refresh)",
            IsActive = false
        });
        await context.SaveChangesAsync(CancellationToken.None);

        var (googleClient, cache, encryption) = CreateMocks(new OAuthStateEntry(userId, "verifier-abc"));
        var handler = new GoogleOAuthCallbackCommandHandler(context, googleClient.Object, cache.Object, encryption.Object);

        await handler.Handle(new GoogleOAuthCallbackCommand("valid-code", "state-xyz"), CancellationToken.None);

        Assert.Equal(1, await context.CalendarIntegrations.CountAsync());
        var stored = await context.CalendarIntegrations.SingleAsync();
        Assert.True(stored.IsActive);
        Assert.Equal("google-sub-123", stored.ExternalAccountId);
    }
}
