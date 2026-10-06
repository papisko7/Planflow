using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Application.Teams.Common;
using PlanFlow.Domain.Authorization;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Teams.Commands.ChangeMemberRole;

public class ChangeMemberRoleCommandHandler : IRequestHandler<ChangeMemberRoleCommand, TeamMemberDto>
{
    private readonly IApplicationDbContext _context;
    private readonly INotificationService _notifications;

    public ChangeMemberRoleCommandHandler(IApplicationDbContext context, INotificationService notifications)
    {
        _context = context;
        _notifications = notifications;
    }

    public async Task<TeamMemberDto> Handle(ChangeMemberRoleCommand request, CancellationToken cancellationToken)
    {
        var requesting = await _context.TeamMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.TeamId == request.TeamId && m.UserId == request.RequestingUserId, cancellationToken)
            ?? throw new NotFoundException(nameof(TeamMember), request.RequestingUserId);

        if (!RoleDefinitions.HasPermission(requesting.Role, Permission.ManageMembers))
        {
            throw new ForbiddenAccessException("You do not have permission to manage this team's members.");
        }

        var target = await _context.TeamMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.TeamId == request.TeamId && m.UserId == request.MemberUserId, cancellationToken)
            ?? throw new NotFoundException(nameof(TeamMember), request.MemberUserId);

        if (request.RequestingUserId == request.MemberUserId)
        {
            throw new ForbiddenAccessException("You cannot change your own role.");
        }

        if (target.Role == TeamRole.Owner || request.NewRole == TeamRole.Owner)
        {
            throw new ForbiddenAccessException("A team has exactly one owner, and its role cannot be changed.");
        }

        // Same rule as adding/removing admins: handing out or taking away Admin is reserved for the owner.
        var touchesAdmin = target.Role == TeamRole.Admin || request.NewRole == TeamRole.Admin;
        if (touchesAdmin && requesting.Role != TeamRole.Owner)
        {
            throw new ForbiddenAccessException("Only the team owner can grant or remove the admin role.");
        }

        var oldRole = target.Role;
        if (oldRole == request.NewRole)
        {
            return TeamMemberDto.FromEntity(target); // nothing to change, nothing to e-mail
        }

        target.Role = request.NewRole;
        target.UpdatedAtUtc = DateTime.UtcNow;

        await _notifications.NotifyAsync(
            NotificationEvent.TeamRoleChanged,
            new[] { target.UserId },
            EmailTemplates.TeamRoleChanged(
                await NotificationLookups.TeamNameAsync(_context, request.TeamId, cancellationToken),
                oldRole,
                request.NewRole,
                await NotificationLookups.UserNameAsync(_context, request.RequestingUserId, cancellationToken)),
            actorUserId: request.RequestingUserId,
            cancellationToken: cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        return TeamMemberDto.FromEntity(target);
    }
}
