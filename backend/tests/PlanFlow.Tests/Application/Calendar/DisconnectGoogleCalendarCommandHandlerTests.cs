using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Calendar.Commands.DisconnectGoogleCalendar;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;
using Xunit;

namespace PlanFlow.Tests.Application.Calendar;

public class DisconnectGoogleCalendarCommandHandlerTests
{
    private static PlanFlowDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<PlanFlowDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new PlanFlowDbContext(options);
    }

    [Fact]
    public async Task Handle_ActiveIntegration_SoftDeletesAndWipesTokens()
    {
        var context = CreateInMemoryContext();
        var userId = Guid.NewGuid();
        var integration = new CalendarIntegration
        {
            UserId = userId,
            Provider = CalendarProvider.Google,
            ExternalAccountId = "sub-1",
            ExternalAccountEmail = "user@example.com",
            EncryptedAccessToken = "enc(access)",
            EncryptedRefreshToken = "enc(refresh)",
            IsActive = true
        };
        context.CalendarIntegrations.Add(integration);
        await context.SaveChangesAsync(CancellationToken.None);

        var handler = new DisconnectGoogleCalendarCommandHandler(context);
        await handler.Handle(new DisconnectGoogleCalendarCommand(userId), CancellationToken.None);

        var stored = await context.CalendarIntegrations.SingleAsync();
        Assert.False(stored.IsActive);
        Assert.Equal(string.Empty, stored.EncryptedAccessToken);
        Assert.Equal(string.Empty, stored.EncryptedRefreshToken);
    }

    [Fact]
    public async Task Handle_NoIntegrationForUser_ThrowsNotFoundException()
    {
        var context = CreateInMemoryContext();
        var handler = new DisconnectGoogleCalendarCommandHandler(context);

        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(new DisconnectGoogleCalendarCommand(Guid.NewGuid()), CancellationToken.None));
    }
}
