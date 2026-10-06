using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Teams.Commands.UpdateTeamNotificationSettings;

/// <summary>Mutes (or unmutes) task e-mails and/or chat e-mails coming from one team, for the calling user only.</summary>
public record UpdateTeamNotificationSettingsCommand(Guid TeamId, Guid UserId, bool MuteNotifications, bool MuteChat) : IRequest;

public class UpdateTeamNotificationSettingsCommandValidator : AbstractValidator<UpdateTeamNotificationSettingsCommand>
{
    public UpdateTeamNotificationSettingsCommandValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}

public class UpdateTeamNotificationSettingsCommandHandler : IRequestHandler<UpdateTeamNotificationSettingsCommand>
{
    private readonly IApplicationDbContext _context;

    public UpdateTeamNotificationSettingsCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(UpdateTeamNotificationSettingsCommand request, CancellationToken cancellationToken)
    {
        var membership = await _context.TeamMembers
            .FirstOrDefaultAsync(m => m.TeamId == request.TeamId && m.UserId == request.UserId, cancellationToken)
            ?? throw new NotFoundException(nameof(TeamMember), request.UserId);

        membership.MuteNotifications = request.MuteNotifications;
        membership.MuteChat = request.MuteChat;
        membership.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }
}
