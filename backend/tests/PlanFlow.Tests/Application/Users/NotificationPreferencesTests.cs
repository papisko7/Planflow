using PlanFlow.Application.Users.Commands.UpdateNotificationPreferences;
using PlanFlow.Application.Users.Common;
using PlanFlow.Application.Users.Queries.GetNotificationPreferences;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Tests.Application.Users;

public class NotificationPreferencesTests
{
    [Fact]
    public async Task Get_UserWithoutRow_ReturnsDefaults()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);

        var prefs = await new GetNotificationPreferencesQueryHandler(db)
            .Handle(new GetNotificationPreferencesQuery(user.Id), CancellationToken.None);

        Assert.True(prefs.EmailEnabled);
        Assert.Equal(LoginNotificationMode.NewDevice, prefs.LoginAlerts);
        Assert.True(prefs.TaskAssignments);
        Assert.False(prefs.TeamTasks);
        Assert.False(prefs.ChatDigest);
        Assert.Empty(db.NotificationPreferences); // reading does not create a row
    }

    [Fact]
    public async Task Update_FirstSave_CreatesTheRowAndGetReturnsIt()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        var custom = new NotificationPreferencesDto(false, false, LoginNotificationMode.Off, false, true, true, false, false, true, false);

        await new UpdateNotificationPreferencesCommandHandler(db)
            .Handle(new UpdateNotificationPreferencesCommand(user.Id, custom), CancellationToken.None);
        var loaded = await new GetNotificationPreferencesQueryHandler(db)
            .Handle(new GetNotificationPreferencesQuery(user.Id), CancellationToken.None);

        Assert.Equal(custom, loaded);
        Assert.Single(db.NotificationPreferences);
    }

    [Fact]
    public async Task Update_SecondSave_OverwritesInsteadOfAddingARow()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        var handler = new UpdateNotificationPreferencesCommandHandler(db);
        await handler.Handle(new UpdateNotificationPreferencesCommand(user.Id,
            new NotificationPreferencesDto(true, true, LoginNotificationMode.NewDevice, true, true, false, true, true, false, true)), CancellationToken.None);

        await handler.Handle(new UpdateNotificationPreferencesCommand(user.Id,
            new NotificationPreferencesDto(false, true, LoginNotificationMode.EveryLogin, true, true, false, true, true, false, true)), CancellationToken.None);

        var row = Assert.Single(db.NotificationPreferences);
        Assert.False(row.EmailEnabled);
        Assert.Equal(LoginNotificationMode.EveryLogin, row.LoginAlerts);
    }

    [Fact]
    public async Task Update_OnlyAffectsTheCaller()
    {
        var db = TestDb.Create();
        var ann = await TestDb.AddUserAsync(db);
        var bob = await TestDb.AddUserAsync(db);

        await new UpdateNotificationPreferencesCommandHandler(db).Handle(new UpdateNotificationPreferencesCommand(ann.Id,
            new NotificationPreferencesDto(false, true, LoginNotificationMode.Off, false, false, false, false, false, false, false)), CancellationToken.None);

        var bobs = await new GetNotificationPreferencesQueryHandler(db).Handle(new GetNotificationPreferencesQuery(bob.Id), CancellationToken.None);
        Assert.True(bobs.EmailEnabled);
    }

    [Fact]
    public void Validator_RejectsUnknownLoginMode()
    {
        var bad = new NotificationPreferencesDto(true, true, (LoginNotificationMode)99, true, true, false, true, true, false, true);

        var result = new UpdateNotificationPreferencesCommandValidator().Validate(new UpdateNotificationPreferencesCommand(Guid.NewGuid(), bad));

        Assert.False(result.IsValid);
    }
}
