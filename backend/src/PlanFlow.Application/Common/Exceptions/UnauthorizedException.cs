namespace PlanFlow.Application.Common.Exceptions;

/// <summary>
/// Thrown when credentials (login) or a refresh token are missing/invalid/expired.
/// Mapped to HTTP 401 by the Api layer.
/// </summary>
public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message) : base(message)
    {
    }
}
