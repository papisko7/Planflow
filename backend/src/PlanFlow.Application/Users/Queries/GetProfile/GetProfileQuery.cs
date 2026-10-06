using MediatR;
using PlanFlow.Application.Users.Common;

namespace PlanFlow.Application.Users.Queries.GetProfile;

public record GetProfileQuery(Guid UserId) : IRequest<UserProfileDto>;
