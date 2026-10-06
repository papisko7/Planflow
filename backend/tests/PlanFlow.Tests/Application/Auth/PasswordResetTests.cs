using FluentValidation;
using Moq;
using System.Text.RegularExpressions;
using PlanFlow.Application.Auth.Commands.RequestPasswordReset;
using PlanFlow.Application.Auth.Commands.ResetPassword;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Common.Security;
using PlanFlow.Application.Notifications;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;

namespace PlanFlow.Tests.Application.Auth;

public class PasswordResetTests
{
    private static RequestPasswordResetCommandHandler RequestHandler(PlanFlowDbContext db) =>
        new(db, new NotificationService(db));

    private static ResetPasswordCommandHandler ResetHandler(PlanFlowDbContext db)
    {
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(p => "HASH:" + p);
        return new ResetPasswordCommandHandler(db, hasher.Object, new NotificationService(db));
    }

    /// <summary>Requests a reset and returns the 6-digit code the user would read in the e-mail.</summary>
    private static async Task<string> RequestCodeAsync(PlanFlowDbContext db, User user)
    {
        await RequestHandler(db).Handle(new RequestPasswordResetCommand(user.Email), CancellationToken.None);
        var mail = db.EmailOutbox.OrderByDescending(m => m.CreatedAtUtc).First(m => m.Event == NotificationEvent.PasswordReset);
        return Regex.Match(mail.TextBody, @"\b\d{6}\b").Value;
    }

    [Fact]
    public async Task Request_KnownEmail_StoresHashedCodeAndQueuesMail()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db, "Ann");

        var code = await RequestCodeAsync(db, user);

        Assert.Matches(@"^\d{6}$", code);
        var stored = db.OneTimeCodes.Single();
        Assert.NotEqual(code, stored.CodeHash);                 // never stored in plain text
        Assert.True(OneTimeCodeHasher.Matches(stored.CodeHash, user.Id, code));
        Assert.True(stored.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(14));
        Assert.Equal(user.Email, db.EmailOutbox.Single().ToEmail);
    }

    [Fact]
    public async Task Request_UnknownEmail_SilentlyDoesNothing()
    {
        var db = TestDb.Create();

        await RequestHandler(db).Handle(new RequestPasswordResetCommand("nobody@example.com"), CancellationToken.None);

        Assert.Empty(db.OneTimeCodes);
        Assert.Empty(db.EmailOutbox);
    }

    [Fact]
    public async Task Request_EmailIsMatchedCaseInsensitively()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);

        await RequestHandler(db).Handle(new RequestPasswordResetCommand(user.Email.ToUpperInvariant()), CancellationToken.None);

        Assert.Single(db.OneTimeCodes);
    }

    [Fact]
    public async Task Request_AskedAgainWithinCooldown_IsIgnored()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        await RequestCodeAsync(db, user);

        await RequestHandler(db).Handle(new RequestPasswordResetCommand(user.Email), CancellationToken.None);

        Assert.Single(db.OneTimeCodes);
        Assert.Single(db.EmailOutbox);
    }

    [Fact]
    public async Task Request_AfterCooldown_CancelsThePreviousCode()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        var firstCode = await RequestCodeAsync(db, user);
        db.OneTimeCodes.Single().CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5); // cooldown passed
        await db.SaveChangesAsync();

        var secondCode = await RequestCodeAsync(db, user);

        Assert.Equal(2, db.OneTimeCodes.Count());
        Assert.Single(db.OneTimeCodes, c => c.UsedAtUtc == null);
        // The old code no longer works; the new one does.
        await Assert.ThrowsAsync<ValidationException>(() =>
            ResetHandler(db).Handle(new ResetPasswordCommand(user.Email, firstCode == secondCode ? "000000" : firstCode, "newpassword1"), CancellationToken.None));
        await ResetHandler(db).Handle(new ResetPasswordCommand(user.Email, secondCode, "newpassword1"), CancellationToken.None);
    }

    [Fact]
    public async Task Reset_CorrectCode_ChangesPasswordSignsOutEverywhereAndQueuesNotice()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        db.RefreshSessions.Add(new RefreshSession { UserId = user.Id, TokenHash = "a", ExpiresAtUtc = DateTime.UtcNow.AddDays(1) });
        db.RefreshSessions.Add(new RefreshSession { UserId = user.Id, TokenHash = "b", ExpiresAtUtc = DateTime.UtcNow.AddDays(1) });
        await db.SaveChangesAsync();
        var code = await RequestCodeAsync(db, user);

        await ResetHandler(db).Handle(new ResetPasswordCommand(user.Email, code, "brand-new-pass"), CancellationToken.None);

        Assert.Equal("HASH:brand-new-pass", db.Users.Single().PasswordHash);
        Assert.Empty(db.RefreshSessions);
        Assert.NotNull(db.OneTimeCodes.Single().UsedAtUtc);
        Assert.Contains(db.EmailOutbox, m => m.Event == NotificationEvent.PasswordChanged && m.ToEmail == user.Email);
    }

    [Fact]
    public async Task Reset_CodeCannotBeUsedTwice()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        var code = await RequestCodeAsync(db, user);
        await ResetHandler(db).Handle(new ResetPasswordCommand(user.Email, code, "first-new-pass"), CancellationToken.None);

        await Assert.ThrowsAsync<ValidationException>(() =>
            ResetHandler(db).Handle(new ResetPasswordCommand(user.Email, code, "second-new-pass"), CancellationToken.None));

        Assert.Equal("HASH:first-new-pass", db.Users.Single().PasswordHash);
    }

    [Fact]
    public async Task Reset_WrongCode_IsRejectedAndCounted()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        var code = await RequestCodeAsync(db, user);
        var wrong = code == "123456" ? "654321" : "123456";

        await Assert.ThrowsAsync<ValidationException>(() =>
            ResetHandler(db).Handle(new ResetPasswordCommand(user.Email, wrong, "newpassword1"), CancellationToken.None));

        Assert.Equal(1, db.OneTimeCodes.Single().FailedAttempts);
        Assert.Equal("hash", db.Users.Single().PasswordHash); // untouched
    }

    [Fact]
    public async Task Reset_AfterFiveWrongGuesses_EvenTheRightCodeIsLocked()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        var code = await RequestCodeAsync(db, user);
        var wrong = code == "123456" ? "654321" : "123456";
        for (var i = 0; i < OneTimeCode.MaxAttempts; i++)
        {
            await Assert.ThrowsAsync<ValidationException>(() =>
                ResetHandler(db).Handle(new ResetPasswordCommand(user.Email, wrong, "newpassword1"), CancellationToken.None));
        }

        await Assert.ThrowsAsync<ValidationException>(() =>
            ResetHandler(db).Handle(new ResetPasswordCommand(user.Email, code, "newpassword1"), CancellationToken.None));

        Assert.Equal("hash", db.Users.Single().PasswordHash);
    }

    [Fact]
    public async Task Reset_ExpiredCode_IsRejected()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        var code = await RequestCodeAsync(db, user);
        db.OneTimeCodes.Single().ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            ResetHandler(db).Handle(new ResetPasswordCommand(user.Email, code, "newpassword1"), CancellationToken.None));
    }

    [Fact]
    public async Task Reset_UnknownEmail_GivesTheSameErrorAsAWrongCode()
    {
        var db = TestDb.Create();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            ResetHandler(db).Handle(new ResetPasswordCommand("nobody@example.com", "123456", "newpassword1"), CancellationToken.None));

        Assert.Equal("The code is invalid or has expired. Request a new one.", ex.Errors.Single().ErrorMessage);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("abcdef")]
    [InlineData("1234567")]
    public void Validator_RejectsCodesThatAreNotSixDigits(string code)
    {
        var result = new ResetPasswordCommandValidator().Validate(new ResetPasswordCommand("a@b.com", code, "longenough1"));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validator_RejectsShortNewPassword()
    {
        var result = new ResetPasswordCommandValidator().Validate(new ResetPasswordCommand("a@b.com", "123456", "short"));

        Assert.False(result.IsValid);
    }
}
