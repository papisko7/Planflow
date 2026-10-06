using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Users.Common;

/// <summary>The caller's notification settings (see <see cref="NotificationPreference"/> for what each switch means).</summary>
public record NotificationPreferencesDto(
    bool EmailEnabled,
    bool PushEnabled,
    LoginNotificationMode LoginAlerts,
    bool TaskAssignments,
    bool TaskChanges,
    bool TeamTasks,
    bool TaskUrgency,
    bool TeamMembership,
    bool ChatDigest,
    bool CalendarIssues)
{
    public static NotificationPreferencesDto FromEntity(NotificationPreference p) => new(
        p.EmailEnabled, p.PushEnabled, p.LoginAlerts, p.TaskAssignments, p.TaskChanges, p.TeamTasks,
        p.TaskUrgency, p.TeamMembership, p.ChatDigest, p.CalendarIssues);
}
