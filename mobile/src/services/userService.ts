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

// The caller's own Out of office time ahead (real instants), used to warn that a deadline falls while they are away.
export async function getMyAbsences(): Promise<MyAbsence[]> {
  const { data } = await http.get<MyAbsence[]>('/api/users/me/absences');
  return data;
}

// Chooses how much of the personal calendar teammates may see. The server applies it whenever a teammate asks.
export async function updateCalendarSharing(level: CalendarSharingLevel): Promise<void> {
  await http.put('/api/users/me/calendar-sharing', { level });
}

// Changes the password of the signed-in user. This device's own refresh token is sent along so the server keeps this
// device signed in and signs out all the others.
export async function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  const session = await loadSession();
  await http.post('/api/users/me/change-password', {
    currentPassword,
    newPassword,
    refreshToken: session?.refreshToken ?? null,
  });
}

// Confirms the e-mail address with the 6-digit code from the confirmation e-mail.
export async function verifyEmail(code: string): Promise<void> {
  await http.post('/api/users/me/verify-email', { code });
}

// Mails a fresh code (the server ignores a request that comes right after the previous one).
export async function resendEmailVerification(): Promise<void> {
  await http.post('/api/users/me/resend-verification');
}

// The server needs the user's IANA time zone (e.g. "Europe/Warsaw") to know where a day ends. The phone knows it,
// so it is reported once per sign-in; the server ignores a zone it already has.
export function deviceTimeZone(): string | null {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || null;
  } catch {
    return null;
  }
}

let reportedTimeZone: string | null = null;

// Best effort: a failure is simply retried at the next start, it never blocks the app.
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

// After sign-out the next account on this phone must report again.
export function forgetReportedTimeZone(): void {
  reportedTimeZone = null;
}
