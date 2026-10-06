using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Caching;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Domain.Authorization;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Teams.Commands.DeleteTeam;

public class DeleteTeamCommandHandler : IRequestHandler<DeleteTeamCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;
    private readonly INotificationService _notifications;

    public DeleteTeamCommandHandler(IApplicationDbContext context, ICacheService cache, INotificationService notifications)
    {
        _context = context;
        _cache = cache;
        _notifications = notifications;
    }

    public async Task Handle(DeleteTeamCommand request, CancellationToken cancellationToken)
    {
        var requesting = await _context.TeamMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.TeamId == request.TeamId && m.UserId == request.RequestingUserId, cancellationToken)
            ?? throw new NotFoundException(nameof(Team), request.TeamId);

        if (!RoleDefinitions.HasPermission(requesting.Role, Permission.DeleteTeam))
        {
            throw new ForbiddenAccessException("Only the team owner can delete the team.");
        }

        var team = await _context.Teams.FirstOrDefaultAsync(t => t.Id == request.TeamId, cancellationToken)
            ?? throw new NotFoundException(nameof(Team), request.TeamId);

        if (team.IsPersonal)
        {
            throw new ForbiddenAccessException("A personal workspace cannot be deleted.");
        }

        // Everyone in the team (except the owner who is deleting it) is told before the member rows disappear.
        var memberIds = await _context.TeamMembers
            .AsNoTracking()
            .Where(m => m.TeamId == request.TeamId)
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);
        await _notifications.NotifyAsync(
            NotificationEvent.TeamDeleted,
            memberIds,
            EmailTemplates.TeamDeleted(team.Name, await NotificationLookups.UserNameAsync(_context, request.RequestingUserId, cancellationToken)),
            actorUserId: request.RequestingUserId,
            cancellationToken: cancellationToken);

        // Tasks, members and chat messages are removed by the database's cascading foreign keys.
        _context.Teams.Remove(team);
        await _context.SaveChangesAsync(cancellationToken);

        await _cache.RemoveAsync(CacheKeys.TeamTasks(request.TeamId), cancellationToken);
    }
}
