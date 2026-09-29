using Moq;
using PlanFlow.Application.Auth.Commands.RefreshToken;
using PlanFlow.Application.Auth.Commands.Register;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Common.Security;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;

namespace PlanFlow.Tests.Application.Auth;

public class AuthHandlerTests
{
    private static Mock<IJwtTokenService> JwtStub(string refresh = "new-refresh")
    {
        var jwt = new Mock<IJwtTokenService>();
        jwt.Setup(j => j.GenerateAccessToken(It.IsAny<User>(), It.IsAny<TeamRole?>(), It.IsAny<Guid?>()))
            .Returns(("access", DateTime.UtcNow.AddMinutes(15)));
        jwt.Setup(j => j.GenerateRefreshToken()).Returns((refresh, DateTime.UtcNow.AddDays(7)));
        return jwt;
    }

    private static async Task<User> SeedUserWithRefreshAsync(PlanFlowDbContext db, string token, DateTime expiresAt)
    {
        var user = await TestDb.AddUserAsync(db);
        user.RefreshTokenHash = TokenHasher.Hash(token);
        user.RefreshTokenExpiresAtUtc = expiresAt;
        await db.SaveChangesAsync();
        return user;
    }

    // ---- Register ----

    [Fact]
    public async Task Register_NewEmail_HashesPasswordAndStoresRefreshTokenHash()
    {
        var db = TestDb.Create();
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.Hash("pw")).Returns("HASHED");
        var handler = new RegisterCommandHandler(db, hasher.Object, JwtStub("rt").Object);

        var result = await handler.Handle(new RegisterCommand("a@b.com", "pw", "Ann"), CancellationToken.None);

        var stored = db.Users.Single();
        Assert.Equal("HASHED", stored.PasswordHash);
        Assert.Equal(TokenHasher.Hash("rt"), stored.RefreshTokenHash);
        Assert.Equal("access", result.AccessToken);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ThrowsConflict()
    {
        var db = TestDb.Create();
        var existing = await TestDb.AddUserAsync(db);
        var handler = new RegisterCommandHandler(db, Mock.Of<IPasswordHasher>(), JwtStub().Object);

        await Assert.ThrowsAsync<ConflictException>(
            () => handler.Handle(new RegisterCommand(existing.Email, "pw", "X"), CancellationToken.None));
    }

    // ---- RefreshToken ----

    [Fact]
    public async Task Refresh_ValidToken_RotatesRefreshToken()
    {
        var db = TestDb.Create();
        var user = await SeedUserWithRefreshAsync(db, "old", DateTime.UtcNow.AddDays(1));
        var handler = new RefreshTokenCommandHandler(db, JwtStub("rotated").Object);

        await handler.Handle(new RefreshTokenCommand(user.Id, "old"), CancellationToken.None);

        Assert.Equal(TokenHasher.Hash("rotated"), db.Users.Single().RefreshTokenHash);
    }

    [Fact]
    public async Task Refresh_OldTokenReusedAfterRotation_IsRejected()
    {
        var db = TestDb.Create();
        var user = await SeedUserWithRefreshAsync(db, "old", DateTime.UtcNow.AddDays(1));
        var handler = new RefreshTokenCommandHandler(db, JwtStub("rotated").Object);
        await handler.Handle(new RefreshTokenCommand(user.Id, "old"), CancellationToken.None);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => handler.Handle(new RefreshTokenCommand(user.Id, "old"), CancellationToken.None));
    }

    [Fact]
    public async Task Refresh_WrongToken_ThrowsUnauthorized()
    {
        var db = TestDb.Create();
        var user = await SeedUserWithRefreshAsync(db, "real", DateTime.UtcNow.AddDays(1));
        var handler = new RefreshTokenCommandHandler(db, JwtStub().Object);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => handler.Handle(new RefreshTokenCommand(user.Id, "fake"), CancellationToken.None));
    }

    [Fact]
    public async Task Refresh_ExpiredToken_ThrowsUnauthorized()
    {
        var db = TestDb.Create();
        var user = await SeedUserWithRefreshAsync(db, "t", DateTime.UtcNow.AddMinutes(-1));
        var handler = new RefreshTokenCommandHandler(db, JwtStub().Object);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => handler.Handle(new RefreshTokenCommand(user.Id, "t"), CancellationToken.None));
    }

    [Fact]
    public async Task Refresh_UnknownUser_ThrowsUnauthorized()
    {
        var handler = new RefreshTokenCommandHandler(TestDb.Create(), JwtStub().Object);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => handler.Handle(new RefreshTokenCommand(Guid.NewGuid(), "t"), CancellationToken.None));
    }

    [Fact]
    public async Task Refresh_WithTeamUserIsNotMemberOf_ThrowsForbidden()
    {
        var db = TestDb.Create();
        var user = await SeedUserWithRefreshAsync(db, "t", DateTime.UtcNow.AddDays(1));
        var otherTeam = await TestDb.AddTeamAsync(db);
        var handler = new RefreshTokenCommandHandler(db, JwtStub().Object);

        await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => handler.Handle(new RefreshTokenCommand(user.Id, "t", otherTeam.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Refresh_WithTeamUserBelongsTo_EmbedsTeamRoleInAccessToken()
    {
        var db = TestDb.Create();
        var user = await SeedUserWithRefreshAsync(db, "t", DateTime.UtcNow.AddDays(1));
        var team = await TestDb.AddTeamAsync(db, (user, TeamRole.Owner));
        var jwt = JwtStub();
        var handler = new RefreshTokenCommandHandler(db, jwt.Object);

        await handler.Handle(new RefreshTokenCommand(user.Id, "t", team.Id), CancellationToken.None);

        jwt.Verify(j => j.GenerateAccessToken(It.IsAny<User>(), TeamRole.Owner, team.Id), Times.Once);
    }
}
