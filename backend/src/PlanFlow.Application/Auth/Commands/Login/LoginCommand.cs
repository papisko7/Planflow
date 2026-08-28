using MediatR;
using PlanFlow.Application.Auth.Common;

namespace PlanFlow.Application.Auth.Commands.Login;

/// <summary>
/// Authenticates a user. When <paramref name="TeamId"/> is given and the user belongs to that team,
/// the issued access token carries a "team_role" claim for it (see <c>PermissionAuthorizationHandler</c>);
/// full team-switching without re-login is deferred to Phase 4.3.
/// </summary>
public record LoginCommand(string Email, string Password, Guid? TeamId) : IRequest<AuthResultDto>;
