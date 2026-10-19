using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Caching;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Domain.Authorization;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Teams.Commands.RemoveTeamMember;

public class RemoveTeamMemberCommandHandler : IRequestHandler<RemoveTeamMemberCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;
    private readonly INotificationService _notifications;

    public RemoveTeamMemberCommandHandler(IApplicationDbContext context, ICacheService cache, INotificationService notifications)
    {
        _context = context;
        _cache = cache;
        _notifications = notifications;
    }

    public async Task Handle(RemoveTeamMemberCommand request, CancellationToken cancellationToken)
    {
        var requesting = await _context.TeamMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.TeamId == request.TeamId && m.UserId == request.RequestingUserId, cancellationToken)
            ?? throw new NotFoundException(nameof(TeamMember), request.RequestingUserId);

        var target = await _context.TeamMembers
            .FirstOrDefaultAsync(m => m.TeamId == request.TeamId && m.UserId == request.MemberUserId, cancellationToken)
            ?? throw new NotFoundException(nameof(TeamMember), request.MemberUserId);

        if (target.Role == TeamRole.Owner)
        {
            throw new ForbiddenAccessException("The team owner cannot be removed.");
        }

        var isSelf = request.RequestingUserId == request.MemberUserId;
        if (!isSelf)
        {
            if (!RoleDefinitions.HasPermission(requesting.Role, Permission.ManageMembers))
            {
                throw new ForbiddenAccessException("You do not have permission to manage this team's members.");
            }

            if (target.Role == TeamRole.Admin && requesting.Role != TeamRole.Owner)
            {
                throw new ForbiddenAccessException("Only the team owner can remove admins.");
            }
        }

        // Tasks stay with the team but lose their assignee — otherwise they would point at someone
        // who can no longer see them.
        var assignedTasks = await _context.CalendarItems
            .Where(t => t.TeamId == request.TeamId && t.AssignedUserId == request.MemberUserId)
            .ToListAsync(cancellationToken);

        var nowUtc = DateTime.UtcNow;
        foreach (var task in assignedTasks)
        {
            task.AssignedUserId = null;
            task.AssignedByUserId = null;
            task.IsAssignmentLocked = false;
            task.UpdatedAtUtc = nowUtc;
        }

        // Someone who left the team no longer takes part in its events and tasks as a guest / co-contractor either.
        var participations = await _context.ItemParticipants
            .Where(p => p.CalendarItem.TeamId == request.TeamId && p.UserId == request.MemberUserId)
            .ToListAsync(cancellationToken);
        _context.ItemParticipants.RemoveRange(participations);

        _context.TeamMembers.Remove(target);

        // Leaving on your own needs no e-mail (the actor is never notified of their own action).
        await _notifications.NotifyAsync(
            NotificationEvent.RemovedFromTeam,
            new[] { request.MemberUserId },
            EmailTemplates.RemovedFromTeam(
                await NotificationLookups.TeamNameAsync(_context, request.TeamId, cancellationToken),
                await NotificationLookups.UserNameAsync(_context, request.RequestingUserId, cancellationToken)),
            actorUserId: request.RequestingUserId,
            cancellationToken: cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        await _cache.RemoveAsync(CacheKeys.TeamTasks(request.TeamId), cancellationToken);
    }
}
