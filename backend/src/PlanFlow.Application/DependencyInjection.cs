using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using PlanFlow.Application.Common.Behaviors;

namespace PlanFlow.Application;

/// <summary>
/// Composition root for the Application layer: registers MediatR handlers, FluentValidation
/// validators, and the validation pipeline behavior so <c>Program.cs</c> only needs one call.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        return services;
    }
}
