using Moq;
using PlanFlow.Application.Auth.Commands.Login;
using PlanFlow.Application.Auth.Commands.Register;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;

namespace PlanFlow.Tests.Application.Auth;

/// <summary>The e-mails sent around registration and sign-in: welcome, and the "new device" alert.</summary>
public class AuthNotificationTests
{
    private static Mock<IJwtTokenService> Jwt()
    {
        var jwt = new Mock<IJwtTokenService>();
        jwt.Setup(j => j.GenerateAccessToken(It.IsAny<User>(), It.IsAny<TeamRole?>(), It.IsAny<Guid?>()))
            .Returns(("access", DateTime.UtcNow.AddMinutes(15)));
        jwt.Setup(j => j.GenerateRefreshToken()).Returns((Guid.NewGuid().ToString(), DateTime.UtcNow.AddDays(7)));
        return jwt;
    }

    private static Mock<IPasswordHasher> AcceptingHasher()
    {
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("HASHED");
        hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        return hasher;
    }

    /// <param name="confirmEmail">Sign-in alerts only go to a confirmed address, so most tests confirm it right after registering.</param>
    private static async Task Register(PlanFlowDbContext db, string email = "ann@example.com", string? deviceId = null, bool confirmEmail = true)
    {
        await new RegisterCommandHandler(db, AcceptingHasher().Object, Jwt().Object, new NotificationService(db))
            .Handle(new RegisterCommand(email, "password1", "Ann", deviceId, deviceId is null ? null : "Pixel 8"), CancellationToken.None);

        if (confirmEmail)
        {
            db.Users.Single(u => u.Email == email).EmailVerifiedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    private static Task Login(PlanFlowDbContext db, string email = "ann@example.com", string? deviceId = null, string? deviceName = null) =>
        new LoginCommandHandler(db, AcceptingHasher().Object, Jwt().Object, new NotificationService(db))
            .Handle(new LoginCommand(email, "password1", null, deviceId, deviceName), CancellationToken.None);

    [Fact]
    public async Task Register_QueuesWelcomeMail()
    {
        var db = TestDb.Create();

        await Register(db);

        var mail = Assert.Single(db.EmailOutbox);
        Assert.Equal(NotificationEvent.Welcome, mail.Event);
        Assert.Equal("ann@example.com", mail.ToEmail);
        Assert.Contains("Ann", mail.TextBody);
    }

    [Fact]
    public async Task Register_WithDevice_RemembersItAsTheFirstKnownDevice()
    {
        var db = TestDb.Create();

        await Register(db, deviceId: "device-1");

        var device = Assert.Single(db.UserDevices);
        Assert.Equal("device-1", device.DeviceId);
        Assert.Equal("Pixel 8", device.Name);
    }

    [Fact]
    public async Task Register_DuplicateEmail_QueuesNoMail()
    {
        var db = TestDb.Create();
        await Register(db);
        var before = db.EmailOutbox.Count();

        await Assert.ThrowsAsync<PlanFlow.Application.Common.Exceptions.ConflictException>(() => Register(db));

        Assert.Equal(before, db.EmailOutbox.Count());
    }

    [Fact]
    public async Task Login_FromTheRegisteringDevice_SendsNoAlert()
    {
        var db = TestDb.Create();
        await Register(db, deviceId: "device-1");

        await Login(db, deviceId: "device-1", deviceName: "Pixel 8");

        Assert.DoesNotContain(db.EmailOutbox, m => m.Event == NotificationEvent.NewDeviceLogin);
    }

    [Fact]
    public async Task Login_FromAnotherDevice_SendsNewDeviceAlertWithItsName()
    {
        var db = TestDb.Create();
        await Register(db, deviceId: "device-1");

        await Login(db, deviceId: "device-2", deviceName: "iPhone 15");

        var alert = Assert.Single(db.EmailOutbox, m => m.Event == NotificationEvent.NewDeviceLogin);
        Assert.Contains("iPhone 15", alert.TextBody);
        Assert.Equal(2, db.UserDevices.Count());
    }

    [Fact]
    public async Task Login_SameNewDeviceTwice_AlertsOnlyTheFirstTime()
    {
        var db = TestDb.Create();
        await Register(db, deviceId: "device-1");

        await Login(db, deviceId: "device-2", deviceName: "iPhone 15");
        await Login(db, deviceId: "device-2", deviceName: "iPhone 15");

        Assert.Single(db.EmailOutbox, m => m.Event == NotificationEvent.NewDeviceLogin);
    }

    [Fact]
    public async Task Login_AccountWithNoDeviceHistory_DoesNotAlertOnItsFirstRecordedDevice()
    {
        var db = TestDb.Create();
        await Register(db); // no device id at registration, e.g. an account created before this feature

        await Login(db, deviceId: "device-1", deviceName: "Pixel 8");

        Assert.Empty(db.EmailOutbox.Where(m => m.Event == NotificationEvent.NewDeviceLogin));
        Assert.Single(db.UserDevices);
    }

    [Fact]
    public async Task Login_WithoutDeviceId_NeverCountsAsNewDevice()
    {
        var db = TestDb.Create();
        await Register(db, deviceId: "device-1");

        await Login(db);

        Assert.DoesNotContain(db.EmailOutbox, m => m.Event == NotificationEvent.NewDeviceLogin);
    }

    [Fact]
    public async Task Login_EveryLoginMode_AlertsEvenOnAKnownDevice()
    {
        var db = TestDb.Create();
        await Register(db, deviceId: "device-1");
        db.NotificationPreferences.Add(new NotificationPreference { UserId = db.Users.Single().Id, LoginAlerts = LoginNotificationMode.EveryLogin });
        await db.SaveChangesAsync();

        await Login(db, deviceId: "device-1", deviceName: "Pixel 8");

        Assert.Single(db.EmailOutbox, m => m.Event == NotificationEvent.NewDeviceLogin);
    }

    [Fact]
    public async Task Login_AlertsOff_NeverAlerts()
    {
        var db = TestDb.Create();
        await Register(db, deviceId: "device-1");
        db.NotificationPreferences.Add(new NotificationPreference { UserId = db.Users.Single().Id, LoginAlerts = LoginNotificationMode.Off });
        await db.SaveChangesAsync();

        await Login(db, deviceId: "device-2", deviceName: "iPhone 15");

        Assert.DoesNotContain(db.EmailOutbox, m => m.Event == NotificationEvent.NewDeviceLogin);
    }

    [Fact]
    public async Task Login_MasterSwitchOff_NeverAlerts()
    {
        var db = TestDb.Create();
        await Register(db, deviceId: "device-1");
        db.NotificationPreferences.Add(new NotificationPreference { UserId = db.Users.Single().Id, EmailEnabled = false });
        await db.SaveChangesAsync();

        await Login(db, deviceId: "device-2", deviceName: "iPhone 15");

        Assert.DoesNotContain(db.EmailOutbox, m => m.Event == NotificationEvent.NewDeviceLogin);
    }
}
