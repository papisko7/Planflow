using MediatR;
using PlanFlow.Application.Users.Common;

namespace PlanFlow.Application.Users.Commands.UpdateProfile;

/// <summary>
/// Changes the caller's display name and/or login e-mail. Changing the e-mail also requires the current
/// password, so someone holding an unlocked phone cannot silently take over the account.
/// </summary>
public record UpdateProfileCommand(Guid UserId, string DisplayName, string Email, string? CurrentPassword) : IRequest<UserProfileDto>;
