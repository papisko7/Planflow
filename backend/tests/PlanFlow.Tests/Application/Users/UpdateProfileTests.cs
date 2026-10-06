using FluentValidation;
using Moq;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Users.Commands.UpdateProfile;

namespace PlanFlow.Tests.Application.Users;

public class UpdateProfileTests
{
    // Accepts only the password "secret" for the stored hash "hash".
    private static IPasswordHasher Hasher()
    {
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.Verify("hash", "secret")).Returns(true);
        return hasher.Object;
    }

    [Fact]
    public async Task ChangeDisplayName_DoesNotNeedPassword()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);

        var dto = await new UpdateProfileCommandHandler(db, Hasher(), new PlanFlow.Application.Notifications.NotificationService(db)).Handle(
            new UpdateProfileCommand(user.Id, " New Nick ", user.Email, null), CancellationToken.None);

        Assert.Equal("New Nick", dto.DisplayName);
        Assert.Equal(user.Email, dto.Email);
    }

    [Fact]
    public async Task ChangeEmail_WithCorrectPassword_Succeeds()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);

        var dto = await new UpdateProfileCommandHandler(db, Hasher(), new PlanFlow.Application.Notifications.NotificationService(db)).Handle(
            new UpdateProfileCommand(user.Id, user.DisplayName, "new@example.com", "secret"), CancellationToken.None);

        Assert.Equal("new@example.com", dto.Email);
        Assert.Equal("new@example.com", db.Users.Single().Email);
    }

    [Fact]
    public async Task ChangeEmail_WithWrongPassword_ThrowsValidation()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);

        await Assert.ThrowsAsync<ValidationException>(() => new UpdateProfileCommandHandler(db, Hasher(), new PlanFlow.Application.Notifications.NotificationService(db)).Handle(
            new UpdateProfileCommand(user.Id, user.DisplayName, "new@example.com", "wrong"), CancellationToken.None));
    }

    [Fact]
    public async Task ChangeEmail_ToExistingOne_ThrowsConflict()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        var other = await TestDb.AddUserAsync(db);

        await Assert.ThrowsAsync<ConflictException>(() => new UpdateProfileCommandHandler(db, Hasher(), new PlanFlow.Application.Notifications.NotificationService(db)).Handle(
            new UpdateProfileCommand(user.Id, user.DisplayName, other.Email.ToUpper(), "secret"), CancellationToken.None));
    }
}
