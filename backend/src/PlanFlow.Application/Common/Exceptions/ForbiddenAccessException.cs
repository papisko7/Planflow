namespace PlanFlow.Application.Common.Exceptions;

/// <summary>
/// Thrown by a handler when the acting user is not a member of the team a request targets.
/// Mapped to HTTP 403 by the Api layer (Phase 1.4); fine-grained RBAC checks stay in
/// <see cref="PlanFlow.Domain.Authorization.RoleDefinitions"/> at the endpoint level.
/// </summary>
public class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException(string message) : base(message)
    {
    }
}
