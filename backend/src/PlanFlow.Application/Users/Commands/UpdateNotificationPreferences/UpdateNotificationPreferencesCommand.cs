using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Users.Common;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Users.Commands.UpdateNotificationPreferences;

/// <summary>Replaces the caller's whole notification settings with the given ones.</summary>
public record UpdateNotificationPreferencesCommand(Guid UserId, NotificationPreferencesDto Preferences) : IRequest<NotificationPreferencesDto>;

public class UpdateNotificationPreferencesCommandValidator : AbstractValidator<UpdateNotificationPreferencesCommand>
{
    public UpdateNotificationPreferencesCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Preferences.LoginAlerts).IsInEnum();
    }
}

public class UpdateNotificationPreferencesCommandHandler : IRequestHandler<UpdateNotificationPreferencesCommand, NotificationPreferencesDto>
{
    private readonly IApplicationDbContext _context;

    public UpdateNotificationPreferencesCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<NotificationPreferencesDto> Handle(UpdateNotificationPreferencesCommand request, CancellationToken cancellationToken)
    {
        var preferences = await _context.NotificationPreferences
            .FirstOrDefaultAsync(p => p.UserId == request.UserId, cancellationToken);

        if (preferences is null)
        {
            preferences = NotificationPreference.CreateDefault(request.UserId);
            _context.NotificationPreferences.Add(preferences);
        }

        var dto = request.Preferences;

        // Turning the chat digest ON starts from now: old messages must not suddenly arrive as a digest.
        if (dto.ChatDigest && !preferences.ChatDigest)
        {
            preferences.ChatSeenUpToUtc = DateTime.UtcNow;
        }

        preferences.EmailEnabled = dto.EmailEnabled;
        preferences.PushEnabled = dto.PushEnabled;
        preferences.LoginAlerts = dto.LoginAlerts;
        preferences.TaskAssignments = dto.TaskAssignments;
        preferences.TaskChanges = dto.TaskChanges;
        preferences.TeamTasks = dto.TeamTasks;
        preferences.TaskUrgency = dto.TaskUrgency;
        preferences.TeamMembership = dto.TeamMembership;
        preferences.ChatDigest = dto.ChatDigest;
        preferences.CalendarIssues = dto.CalendarIssues;
        preferences.IncompleteItemsReminder = dto.IncompleteItemsReminder;
        preferences.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return NotificationPreferencesDto.FromEntity(preferences);
    }
}
