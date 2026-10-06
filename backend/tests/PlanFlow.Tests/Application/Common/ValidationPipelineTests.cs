using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using PlanFlow.Application;
using PlanFlow.Application.Auth.Commands.ChangePassword;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications.Inbox;
using PlanFlow.Infrastructure.Persistence;

namespace PlanFlow.Tests.Application.Common;

/// <summary>Validators must run for every request, including commands that return nothing (they are a different MediatR shape).</summary>
public class ValidationPipelineTests
{
    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationServices();
        var db = TestDb.Create();
        services.AddSingleton<PlanFlowDbContext>(db);
        services.AddSingleton<IApplicationDbContext>(db);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task CommandWithoutAResult_IsValidatedBeforeItsHandlerRuns()
    {
        using var provider = Provider();
        var sender = provider.GetRequiredService<ISender>();

        await Assert.ThrowsAsync<ValidationException>(() =>
            sender.Send(new RegisterPushTokenCommand(Guid.NewGuid(), "garbage", "ios")));
    }

    [Fact]
    public async Task ChangePassword_ShortNewPassword_IsRejectedByTheValidator()
    {
        using var provider = Provider();
        var sender = provider.GetRequiredService<ISender>();

        await Assert.ThrowsAsync<ValidationException>(() =>
            sender.Send(new ChangePasswordCommand(Guid.NewGuid(), "old-password-1", "short", null)));
    }
}
