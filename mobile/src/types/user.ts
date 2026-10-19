import type { CalendarSharingLevel } from './availability';

// Mirrors PlanFlow.Application.Users.Common.UserProfileDto
export interface UserProfileDto {
  id: string;
  email: string;
  displayName: string;
  // False until the owner types the code from the confirmation e-mail; optional mails only reach confirmed addresses.
  emailVerified: boolean;
  // IANA zone reported by the phone (null until the first sign-in after the update).
  timeZoneId?: string | null;
  // What teammates may see of the personal (Google) calendar: nothing, availability only, or details.
  calendarSharingLevel?: CalendarSharingLevel;
}

// Mirrors PlanFlow.Api.Contracts.UpdateProfileRequest; currentPassword is only needed when the e-mail changes.
export interface UpdateProfileRequest {
  displayName: string;
  email: string;
  currentPassword: string | null;
}
