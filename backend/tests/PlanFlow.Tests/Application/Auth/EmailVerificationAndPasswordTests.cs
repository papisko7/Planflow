using System.Text.RegularExpressions;
using FluentValidation;
using Moq;
using PlanFlow.Application.Auth.Commands.ChangePassword;
using PlanFlow.Application.Auth.Commands.Register;
using PlanFlow.Application.Auth.Commands.RequestPasswordReset;
using PlanFlow.Application.Auth.Commands.ResendEmailVerification;
using PlanFlow.Application.Auth.Commands.ResetPassword;
using PlanFlow.Application.Auth.Commands.VerifyEmail;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Common.Security;
using PlanFlow.Application.Notifications;
using PlanFlow.Application.Users.Commands.UpdateProfile;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;
using PlanFlow.Infrastructure.Persistence;

namespace PlanFlow.Tests.Application.Auth;

/// <summary>E-mail confirmation after sign-up, and changing the password while signed in.</summary>
public class EmailVerificationAndPasswordTests
{
    private static Mock<IPasswordHasher> Hasher()
    {
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(p => "HASH:" + p);
        hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns<string, string>((hash, p) => hash == "HASH:" + p);
        return hasher;
    }

    private static Mock<IJwtTokenService> Jwt()
    {
        var jwt = new Mock<IJwtTokenService>();
        jwt.Setup(j => j.GenerateAccessToken(It.IsAny<User>(), It.IsAny<TeamRole?>(), It.IsAny<Guid?>()))
            .Returns(("access", DateTime.UtcNow.AddMinutes(15)));
        jwt.Setup(j => j.GenerateRefreshToken()).Returns((Guid.NewGuid().ToString(), DateTime.UtcNow.AddDays(7)));
        return jwt;
    }

    private static string CodeFromMail(EmailOutboxMessage mail) => Regex.Match(mail.TextBody, @"\b\d{6}\b").Value;

    /// <summary>Registers a user and returns the confirmation code from the welcome e-mail.</summary>
    private static async Task<(PlanFlowDbContext Db, User User, string Code)> RegisteredUserAsync()
    {
        var db = TestDb.Create();
        await new RegisterCommandHandler(db, Hasher().Object, Jwt().Object, new NotificationService(db))
            .Handle(new RegisterCommand("ann@example.com", "password1", "Ann", null, null), CancellationToken.None);
        var welcome = db.EmailOutbox.Single(m => m.Event == NotificationEvent.Welcome);
        return (db, db.Users.Single(), CodeFromMail(welcome));
    }

    private static VerifyEmailCommandHandler Verifier(PlanFlowDbContext db) => new(db);

    private static ResendEmailVerificationCommandHandler Resender(PlanFlowDbContext db) => new(db, new NotificationService(db));

    // ---------- Registration ----------

    [Fact]
    public async Task Register_CreatesUnconfirmedAccount_AndWelcomeMailCarriesTheCode()
    {
        var (db, user, code) = await RegisteredUserAsync();

        Assert.False(user.IsEmailVerified);
        Assert.Matches(@"^\d{6}$", code);
        var stored = Assert.Single(db.OneTimeCodes);
        Assert.Equal(OneTimeCodePurpose.EmailVerification, stored.Purpose);
        Assert.True(OneTimeCodeHasher.Matches(stored.CodeHash, user.Id, code));
    }

    // ---------- Verify ----------

    [Fact]
    public async Task Verify_CorrectCode_ConfirmsTheAddress()
    {
        var (db, user, code) = await RegisteredUserAsync();

        await Verifier(db).Handle(new VerifyEmailCommand(user.Id, code), CancellationToken.None);

        Assert.True(db.Users.Single().IsEmailVerified);
        Assert.NotNull(db.OneTimeCodes.Single().UsedAtUtc);
    }

    [Fact]
    public async Task Verify_WrongCode_IsRejectedAndCounted()
    {
        var (db, user, code) = await RegisteredUserAsync();
        var wrong = code == "000000" ? "111111" : "000000";

        await Assert.ThrowsAsync<ValidationException>(() => Verifier(db).Handle(new VerifyEmailCommand(user.Id, wrong), CancellationToken.None));

        Assert.False(db.Users.Single().IsEmailVerified);
        Assert.Equal(1, db.OneTimeCodes.Single().FailedAttempts);
    }

    [Fact]
    public async Task Verify_AfterFiveWrongGuesses_EvenTheRightCodeIsLocked()
    {
        var (db, user, code) = await RegisteredUserAsync();
        var wrong = code == "000000" ? "111111" : "000000";
        for (var i = 0; i < OneTimeCode.MaxAttempts; i++)
        {
            await Assert.ThrowsAsync<ValidationException>(() => Verifier(db).Handle(new VerifyEmailCommand(user.Id, wrong), CancellationToken.None));
        }

        await Assert.ThrowsAsync<ValidationException>(() => Verifier(db).Handle(new VerifyEmailCommand(user.Id, code), CancellationToken.None));

        Assert.False(db.Users.Single().IsEmailVerified);
    }

    [Fact]
    public async Task Verify_ExpiredCode_IsRejected()
    {
        var (db, user, code) = await RegisteredUserAsync();
        db.OneTimeCodes.Single().ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => Verifier(db).Handle(new VerifyEmailCommand(user.Id, code), CancellationToken.None));
    }

    [Fact]
    public async Task Verify_AlreadyConfirmed_IsANoOp()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db); // helper users are confirmed

        await Verifier(db).Handle(new VerifyEmailCommand(user.Id, "123456"), CancellationToken.None);

        Assert.True(db.Users.Single().IsEmailVerified);
    }

    [Fact]
    public async Task Verify_APasswordResetCodeCannotConfirmTheAddress()
    {
        var (db, user, _) = await RegisteredUserAsync();
        await new RequestPasswordResetCommandHandler(db, new NotificationService(db))
            .Handle(new RequestPasswordResetCommand(user.Email), CancellationToken.None);
        var resetCode = CodeFromMail(db.EmailOutbox.Single(m => m.Event == NotificationEvent.PasswordReset));

        await Assert.ThrowsAsync<ValidationException>(() => Verifier(db).Handle(new VerifyEmailCommand(user.Id, resetCode), CancellationToken.None));

        Assert.False(db.Users.Single().IsEmailVerified);
    }

    [Fact]
    public async Task Reset_WithTheMailedCode_AlsoConfirmsTheAddress()
    {
        var (db, user, _) = await RegisteredUserAsync();
        await new RequestPasswordResetCommandHandler(db, new NotificationService(db))
            .Handle(new RequestPasswordResetCommand(user.Email), CancellationToken.None);
        var resetCode = CodeFromMail(db.EmailOutbox.Single(m => m.Event == NotificationEvent.PasswordReset));

        await new ResetPasswordCommandHandler(db, Hasher().Object, new NotificationService(db))
            .Handle(new ResetPasswordCommand(user.Email, resetCode, "new-password-1"), CancellationToken.None);

        Assert.True(db.Users.Single().IsEmailVerified);
    }

    // ---------- Resend ----------

    [Fact]
    public async Task Resend_RightAfterRegistering_IsHeldBackByTheCooldown()
    {
        var (db, user, _) = await RegisteredUserAsync();

        await Resender(db).Handle(new ResendEmailVerificationCommand(user.Id), CancellationToken.None);

        Assert.Empty(db.EmailOutbox.Where(m => m.Event == NotificationEvent.EmailVerification));
    }

    [Fact]
    public async Task Resend_AfterTheCooldown_SendsANewCodeAndCancelsTheOldOne()
    {
        var (db, user, oldCode) = await RegisteredUserAsync();
        db.OneTimeCodes.Single().CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5);
        await db.SaveChangesAsync();

        await Resender(db).Handle(new ResendEmailVerificationCommand(user.Id), CancellationToken.None);

        var newCode = CodeFromMail(db.EmailOutbox.Single(m => m.Event == NotificationEvent.EmailVerification));
        await Verifier(db).Handle(new VerifyEmailCommand(user.Id, newCode), CancellationToken.None);
        Assert.True(db.Users.Single().IsEmailVerified);
        Assert.Equal(2, db.OneTimeCodes.Count());
        Assert.NotEqual(oldCode, newCode); // (a clash is a 1-in-a-million coincidence; the old row is cancelled either way)
    }

    [Fact]
    public async Task Resend_AlreadyConfirmed_SendsNothing()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);

        await Resender(db).Handle(new ResendEmailVerificationCommand(user.Id), CancellationToken.None);

        Assert.Empty(db.EmailOutbox);
    }

    // ---------- Who may receive optional mail ----------

    [Fact]
    public void Policy_UnconfirmedAddress_ReceivesOnlyTheMandatoryAccountMails()
    {
        Assert.True(NotificationPolicy.CanReceive(false, NotificationEvent.Welcome));
        Assert.True(NotificationPolicy.CanReceive(false, NotificationEvent.EmailVerification));
        Assert.True(NotificationPolicy.CanReceive(false, NotificationEvent.PasswordReset));
        Assert.False(NotificationPolicy.CanReceive(false, NotificationEvent.TaskAssignedToYou));
        Assert.False(NotificationPolicy.CanReceive(false, NotificationEvent.AddedToTeam));
        Assert.True(NotificationPolicy.CanReceive(true, NotificationEvent.TaskAssignedToYou));
    }

    [Fact]
    public async Task NotifyAsync_SkipsUnconfirmedAddresses()
    {
        var db = TestDb.Create();
        var confirmed = await TestDb.AddUserAsync(db, "Confirmed");
        var stranger = await TestDb.AddUserAsync(db, "Stranger");
        stranger.EmailVerifiedAtUtc = null;
        await db.SaveChangesAsync();

        await new NotificationService(db).NotifyAsync(
            NotificationEvent.AddedToTeam, new[] { confirmed.Id, stranger.Id }, EmailTemplates.AddedToTeam("Team", TeamRole.Member, "Bob"));
        await db.SaveChangesAsync();

        var mail = Assert.Single(db.EmailOutbox);
        Assert.Equal(confirmed.Email, mail.ToEmail);
    }

    // ---------- Changing the e-mail address ----------

    [Fact]
    public async Task ChangeEmail_ResetsConfirmation_AndMailsACodeToTheNewAddress()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        user.PasswordHash = "HASH:secret";
        await db.SaveChangesAsync();

        var dto = await new UpdateProfileCommandHandler(db, Hasher().Object, new NotificationService(db))
            .Handle(new UpdateProfileCommand(user.Id, user.DisplayName, "new@example.com", "secret"), CancellationToken.None);

        Assert.False(dto.EmailVerified);
        var mail = Assert.Single(db.EmailOutbox);
        Assert.Equal("new@example.com", mail.ToEmail);
        Assert.Equal(NotificationEvent.EmailVerification, mail.Event);
        await Verifier(db).Handle(new VerifyEmailCommand(user.Id, CodeFromMail(mail)), CancellationToken.None);
        Assert.True(db.Users.Single().IsEmailVerified);
    }

    [Fact]
    public async Task ChangeDisplayNameOnly_KeepsTheAddressConfirmed()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);

        var dto = await new UpdateProfileCommandHandler(db, Hasher().Object, new NotificationService(db))
            .Handle(new UpdateProfileCommand(user.Id, "New Name", user.Email, null), CancellationToken.None);

        Assert.True(dto.EmailVerified);
        Assert.Empty(db.EmailOutbox);
    }

    // ---------- Change password (signed in) ----------

    private static async Task<(PlanFlowDbContext Db, User User)> UserWithTwoDevicesAsync()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        user.PasswordHash = "HASH:old-password-1";
        db.RefreshSessions.Add(new RefreshSession { UserId = user.Id, TokenHash = TokenHasher.Hash("this-device"), ExpiresAtUtc = DateTime.UtcNow.AddDays(1) });
        db.RefreshSessions.Add(new RefreshSession { UserId = user.Id, TokenHash = TokenHasher.Hash("other-device"), ExpiresAtUtc = DateTime.UtcNow.AddDays(1) });
        await db.SaveChangesAsync();
        return (db, user);
    }

    private static ChangePasswordCommandHandler ChangeHandler(PlanFlowDbContext db) => new(db, Hasher().Object, new NotificationService(db));

    [Fact]
    public async Task ChangePassword_CorrectCurrent_UpdatesHash_KeepsThisDevice_SignsOutTheOthers_AndMails()
    {
        var (db, user) = await UserWithTwoDevicesAsync();

        await ChangeHandler(db).Handle(
            new ChangePasswordCommand(user.Id, "old-password-1", "new-password-1", "this-device"), CancellationToken.None);

        Assert.Equal("HASH:new-password-1", db.Users.Single().PasswordHash);
        var remaining = Assert.Single(db.RefreshSessions);
        Assert.Equal(TokenHasher.Hash("this-device"), remaining.TokenHash);
        var mail = Assert.Single(db.EmailOutbox);
        Assert.Equal(NotificationEvent.PasswordChanged, mail.Event);
        Assert.Contains("all other devices", mail.TextBody);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrent_ChangesNothing()
    {
        var (db, user) = await UserWithTwoDevicesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => ChangeHandler(db).Handle(
            new ChangePasswordCommand(user.Id, "not-my-password", "new-password-1", "this-device"), CancellationToken.None));

        Assert.Equal("HASH:old-password-1", db.Users.Single().PasswordHash);
        Assert.Equal(2, db.RefreshSessions.Count());
        Assert.Empty(db.EmailOutbox);
    }

    [Fact]
    public async Task ChangePassword_WithoutDeviceToken_SignsOutEveryDevice()
    {
        var (db, user) = await UserWithTwoDevicesAsync();

        await ChangeHandler(db).Handle(new ChangePasswordCommand(user.Id, "old-password-1", "new-password-1", null), CancellationToken.None);

        Assert.Empty(db.RefreshSessions);
    }

    [Theory]
    [InlineData("old-password-1", "short")]            // too short
    [InlineData("old-password-1", "old-password-1")]   // same as the current one
    public void ChangePassword_ValidatorRejectsWeakOrUnchangedPasswords(string current, string next)
    {
        var result = new ChangePasswordCommandValidator().Validate(new ChangePasswordCommand(Guid.NewGuid(), current, next, null));

        Assert.False(result.IsValid);
    }
}
