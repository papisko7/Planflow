using FluentValidation;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Users.Commands.UpdateTimeZone;

namespace PlanFlow.Tests.Application.Users;

public class UpdateTimeZoneTests
{
    [Fact]
    public async Task Handle_StoresTheZone()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);

        await new UpdateTimeZoneCommandHandler(db).Handle(new UpdateTimeZoneCommand(user.Id, "Europe/Warsaw"), CancellationToken.None);

        Assert.Equal("Europe/Warsaw", db.Users.Single().TimeZoneId);
    }

    [Fact]
    public async Task Handle_SameZoneAgain_ChangesNothing()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        var handler = new UpdateTimeZoneCommandHandler(db);
        await handler.Handle(new UpdateTimeZoneCommand(user.Id, "Europe/Warsaw"), CancellationToken.None);
        var stamp = db.Users.Single().UpdatedAtUtc;

        await handler.Handle(new UpdateTimeZoneCommand(user.Id, "Europe/Warsaw"), CancellationToken.None);

        Assert.Equal(stamp, db.Users.Single().UpdatedAtUtc);
    }

    [Fact]
    public async Task Handle_UnknownUser_ThrowsNotFound()
    {
        var db = TestDb.Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new UpdateTimeZoneCommandHandler(db).Handle(new UpdateTimeZoneCommand(Guid.NewGuid(), "Europe/Warsaw"), CancellationToken.None));
    }

    [Theory]
    [InlineData("Europe/Warsaw", true)]
    [InlineData("America/New_York", true)]
    [InlineData("UTC", true)]
    [InlineData("", false)]
    [InlineData("Mars/Olympus_Mons", false)]
    [InlineData("not a zone", false)]
    public void Validator_AcceptsOnlyRealZoneNames(string zone, bool valid)
    {
        var result = new UpdateTimeZoneCommandValidator().Validate(new UpdateTimeZoneCommand(Guid.NewGuid(), zone));

        Assert.Equal(valid, result.IsValid);
    }
}
