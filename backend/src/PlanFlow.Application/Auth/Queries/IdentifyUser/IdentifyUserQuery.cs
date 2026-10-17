using MediatR;

namespace PlanFlow.Application.Auth.Queries.IdentifyUser;

/// <summary>
/// Returns the UserId for a given email address so the mobile client can
/// look up per-user PIN state before prompting for credentials.
/// Throws NotFoundException if the email is not registered.
/// </summary>
public record IdentifyUserQuery(string Email) : IRequest<IdentifyUserResult>;

public record IdentifyUserResult(Guid UserId);
