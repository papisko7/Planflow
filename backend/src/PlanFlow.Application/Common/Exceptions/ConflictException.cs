namespace PlanFlow.Application.Common.Exceptions;

/// <summary>
/// Thrown when a request conflicts with existing state (e.g. adding a user who is already a
/// team member). Mapped to HTTP 409 by the Api layer (Phase 1.4).
/// </summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message)
    {
    }
}
