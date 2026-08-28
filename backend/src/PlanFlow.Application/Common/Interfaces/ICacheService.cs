namespace PlanFlow.Application.Common.Interfaces;

/// <summary>
/// Read-through cache abstraction for CQRS query handlers. Declared here (not in Infrastructure)
/// so handlers depend on the abstraction, not the concrete Redis client — mirrors the
/// <see cref="IApplicationDbContext"/> pattern. A failing cache must never fail a request: implementations
/// are expected to swallow transport errors and behave as a cache miss instead.
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class;

    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken) where T : class;

    Task RemoveAsync(string key, CancellationToken cancellationToken);
}
