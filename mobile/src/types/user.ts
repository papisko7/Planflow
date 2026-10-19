import type { CalendarSharingLevel } from './availability';

export interface UserProfileDto {
  id: string;
  email: string;
  displayName: string;
  emailVerified: boolean;
  timeZoneId?: string | null;
  calendarSharingLevel?: CalendarSharingLevel;
}

// Mirrors PlanFlow.Api.Contracts.UpdateProfileRequest; currentPassword is only needed when the e-mail changes.
export interface UpdateProfileRequest {
  displayName: string;
  email: string;
  currentPassword: string | null;
}

export interface MyAbsence {
  itemId: string;
  startUtc: string;
  endUtc: string;
}
