using PlanFlow.Application.Alerts.Commands.CreateAlert;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Tests.Application.Alerts;

public class CreateAlertCommandHandlerTests
{
    [Fact]
    public async Task Handle_PersistsUnreadAlert()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        var type = Enum.GetValues<AlertType>().First();

        var dto = await new CreateAlertCommandHandler(db)
            .Handle(new CreateAlertCommand(user.Id, null, type, "Deadline soon"), CancellationToken.None);

        var stored = db.Alerts.Single();
        Assert.Equal(dto.Id, stored.Id);
        Assert.Equal("Deadline soon", stored.Message);
        Assert.False(stored.IsRead);
    }

    [Fact]
    public void Validator_RejectsEmptyMessage()
    {
        var result = new CreateAlertCommandValidator()
            .Validate(new CreateAlertCommand(Guid.NewGuid(), null, Enum.GetValues<AlertType>().First(), ""));

        Assert.False(result.IsValid);
    }
}
