using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Infrastructure.Persistence;

namespace PlanFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<PlanFlowDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("PlanFlowDb")));

        // Resolve the Application-layer abstraction to the same scoped DbContext instance.
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<PlanFlowDbContext>());

        return services;
    }
}
