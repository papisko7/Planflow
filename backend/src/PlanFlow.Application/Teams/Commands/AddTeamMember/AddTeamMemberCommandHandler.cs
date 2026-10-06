using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Application.Teams.Common;
using PlanFlow.Domain.Authorization;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Teams.Commands.AddTeamMember;

public class AddTeamMemberCommandHandler : IRequestHandler<AddTeamMemberCommand, TeamMemberDto>
{
    private readonly IApplicationDbContext _context;
    private readonly INotificationService _notifications;

    public AddTeamMemberCommandHandler(IApplicationDbContext context, INotificationService notifications)
    {
        _context = context;
        _notifications = notifications;
    }

    public async Task<TeamMemberDto> Handle(AddTeamMemberCommand request, CancellationToken cancellationToken)
    {
        var requestingMembership = await _context.TeamMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.TeamId == request.TeamId && m.UserId == request.RequestingUserId,
                cancellationToken)
            ?? throw new NotFoundException(nameof(TeamMember), request.RequestingUserId);

        if (!RoleDefinitions.HasPermission(requestingMembership.Role, Permission.ManageMembers))
        {
            throw new ForbiddenAccessException("You do not have permission to manage this team's members.");
        }

        var isPersonal = await _context.Teams
            .AnyAsync(t => t.Id == request.TeamId && t.IsPersonal, cancellationToken);
        if (isPersonal)
        {
            throw new ForbiddenAccessException("A personal workspace cannot have other members.");
        }

        // A team has exactly one Owner (its creator), and only the Owner may hand out Admin rights.
        if (request.Role == TeamRole.Owner)
        {
            throw new ForbiddenAccessException("A team can only have one owner.");
        }

        if (request.Role == TeamRole.Admin && requestingMembership.Role != TeamRole.Owner)
        {
            throw new ForbiddenAccessException("Only the team owner can add admins.");
        }

        var email = request.NewMemberEmail.Trim().ToLower();
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email, cancellationToken)
            ?? throw new NotFoundException(nameof(User), request.NewMemberEmail);

        var alreadyMember = await _context.TeamMembers
            .AnyAsync(m => m.TeamId == request.TeamId && m.UserId == user.Id, cancellationToken);

        if (alreadyMember)
        {
            throw new ConflictException("This user is already a member of the team.");
        }

        var membership = new TeamMember
        {
            TeamId = request.TeamId,
            UserId = user.Id,
            Role = request.Role
        };

        _context.TeamMembers.Add(membership);

        await _notifications.NotifyAsync(
            NotificationEvent.AddedToTeam,
            new[] { user.Id },
            EmailTemplates.AddedToTeam(
                await NotificationLookups.TeamNameAsync(_context, request.TeamId, cancellationToken),
                request.Role,
                await NotificationLookups.UserNameAsync(_context, request.RequestingUserId, cancellationToken)),
            actorUserId: request.RequestingUserId,
            cancellationToken: cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        return new TeamMemberDto(membership.Id, membership.UserId, user.DisplayName, membership.Role, membership.JoinedAtUtc);
    }
}
