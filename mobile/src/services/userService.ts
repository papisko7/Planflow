import { http } from './httpClient';
import { loadSession } from './tokenStorage';
import type { CalendarSharingLevel } from '../types/availability';
import type { MyAbsence, UpdateProfileRequest, UserProfileDto } from '../types/user';

export async function getProfile(): Promise<UserProfileDto> {
  const { data } = await http.get<UserProfileDto>('/api/users/me');
  return data;
}

export async function updateProfile(request: UpdateProfileRequest): Promise<UserProfileDto> {
  const { data } = await http.put<UserProfileDto>('/api/users/me', request);
  return data;
}

export async function getMyAbsences(): Promise<MyAbsence[]> {
  const { data } = await http.get<MyAbsence[]>('/api/users/me/absences');
  return data;
}

export async function updateCalendarSharing(level: CalendarSharingLevel): Promise<void> {
  await http.put('/api/users/me/calendar-sharing', { level });
}

export async function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  const session = await loadSession();
  await http.post('/api/users/me/change-password', {
    currentPassword,
    newPassword,
    refreshToken: session?.refreshToken ?? null,
  });
}

export async function verifyEmail(code: string): Promise<void> {
  await http.post('/api/users/me/verify-email', { code });
}

export async function resendEmailVerification(): Promise<void> {
  await http.post('/api/users/me/resend-verification');
}

export function deviceTimeZone(): string | null {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || null;
  } catch {
    return null;
  }
}

let reportedTimeZone: string | null = null;

export async function reportTimeZone(): Promise<void> {
  const timeZoneId = deviceTimeZone();
  if (!timeZoneId || timeZoneId === reportedTimeZone) return;
  try {
    await http.put('/api/users/me/time-zone', { timeZoneId });
    reportedTimeZone = timeZoneId;
  } catch {
    // ignored on purpose, see above
  }
}

export function forgetReportedTimeZone(): void {
  reportedTimeZone = null;
}
