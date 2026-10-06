using MediatR;
using PlanFlow.Application.Auth.Common;

namespace PlanFlow.Application.Auth.Commands.Register;

/// <summary>
/// Creates a new user account, immediately issues a token pair (no team yet — see <see cref="Application.Auth.Commands.Login.LoginCommand"/>)
/// and queues a welcome e-mail. <paramref name="DeviceId"/>/<paramref name="DeviceName"/> let the app register its device up front.
/// </summary>
public record RegisterCommand(string Email, string Password, string DisplayName, string? DeviceId = null, string? DeviceName = null) : IRequest<AuthResultDto>;
