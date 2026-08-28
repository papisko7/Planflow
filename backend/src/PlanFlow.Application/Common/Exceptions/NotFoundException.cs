namespace PlanFlow.Application.Common.Exceptions;

/// <summary>
/// Thrown by a handler when a request references an entity that does not exist.
/// Mapped to HTTP 404 by the Api layer (Phase 1.4).
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string entityName, object key)
        : base($"Entity \"{entityName}\" ({key}) was not found.")
    {
    }
}
