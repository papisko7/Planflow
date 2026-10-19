using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Calendar.Commands.RequestGoogleSync;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;
using Xunit;

namespace PlanFlow.Tests.Application.Calendar;

/// <summary>Step 7.5.9: "Sync now" is the only way Google is fetched, and a repeat press within a minute of the last sync starts nothing.</summary>
public class RequestGoogleSyncTests
{
    private static PlanFlowDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<PlanFlowDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<bool> PressAsync(PlanFlowDbContext db, Guid userId) =>
        await new RequestGoogleSyncCommandHandler(db).Handle(new RequestGoogleSyncCommand(userId), CancellationToken.None);

    private static async Task<Guid> ConnectAsync(PlanFlowDbContext db, DateTime? lastSyncedAtUtc, bool active = true)
    {
        var userId = Guid.NewGuid();
        db.CalendarIntegrations.Add(new CalendarIntegration
        {
            UserId = userId,
            Provider = CalendarProvider.Google,
            ExternalAccountId = "sub-" + userId,
            ExternalAccountEmail = "user@example.com",
            EncryptedAccessToken = "enc(access)",
            EncryptedRefreshToken = "enc(refresh)",
            AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            LastSyncedAtUtc = lastSyncedAtUtc,
            IsActive = active
        });
        await db.SaveChangesAsync();
        return userId;
    }

    [Fact]
    public async Task NeverSynced_MayStart()
    {
        var db = CreateDb();
        Assert.True(await PressAsync(db, await ConnectAsync(db, lastSyncedAtUtc: null)));
    }

    [Fact]
    public async Task SyncedMomentsAgo_StartsNothing()
    {
        var db = CreateDb();
        Assert.False(await PressAsync(db, await ConnectAsync(db, DateTime.UtcNow.AddSeconds(-10))));
    }

    [Fact]
    public async Task SyncedLongEnoughAgo_MayStart()
    {
        var db = CreateDb();
        var justOver = RequestGoogleSyncCommandHandler.MinInterval + TimeSpan.FromSeconds(5);
        Assert.True(await PressAsync(db, await ConnectAsync(db, DateTime.UtcNow - justOver)));
    }

    [Fact]
    public async Task NoGoogleAccount_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => PressAsync(CreateDb(), Guid.NewGuid()));
    }

    [Fact]
    public async Task DisconnectedAccount_IsNotFound()
    {
        var db = CreateDb();
        var userId = await ConnectAsync(db, lastSyncedAtUtc: null, active: false);
        await Assert.ThrowsAsync<NotFoundException>(() => PressAsync(db, userId));
    }
}
