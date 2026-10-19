using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Users.Common;

public record UserProfileDto(
    Guid Id, string Email, string DisplayName, bool EmailVerified, string? TimeZoneId = null,
    CalendarSharingLevel CalendarSharingLevel = CalendarSharingLevel.Availability);
