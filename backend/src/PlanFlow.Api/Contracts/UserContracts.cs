namespace PlanFlow.Api.Contracts;

/// <summary><paramref name="CurrentPassword"/> is only required when <paramref name="Email"/> differs from the current one.</summary>
public record UpdateProfileRequest(string DisplayName, string Email, string? CurrentPassword);

/// <summary><paramref name="RefreshToken"/> is this device's own refresh token: it stays signed in, all other devices are signed out.</summary>
public record ChangePasswordRequest(string CurrentPassword, string NewPassword, string? RefreshToken = null);

/// <summary><paramref name="Code"/> is the 6-digit code from the confirmation e-mail.</summary>
public record VerifyEmailRequest(string Code);

/// <summary><paramref name="TimeZoneId"/> is an IANA name from the device, e.g. "Europe/Warsaw".</summary>
public record UpdateTimeZoneRequest(string TimeZoneId);

/// <summary>How much of the personal calendar teammates may see: 0 = nothing, 1 = availability only, 2 = details.</summary>
public record UpdateCalendarSharingRequest(PlanFlow.Domain.Enums.CalendarSharingLevel Level);
