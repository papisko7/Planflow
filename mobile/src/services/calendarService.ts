import { http } from './httpClient';
import { invalidateAllTasksCache } from './taskCache';

export type GoogleCalendarStatus = {
  connected: boolean;
  email: string | null;
  lastSyncedAtUtc: string | null;
};

export async function getGoogleCalendarStatus(): Promise<GoogleCalendarStatus> {
  const { data } = await http.get<GoogleCalendarStatus>('/api/calendar/google/status');
  return data;
}

// Backend returns the Google consent-screen URL (PKCE + state already baked in); the app only opens it.
export async function getGoogleConnectUrl(): Promise<string> {
  const { data } = await http.get<{ authorizationUrl: string }>('/api/calendar/google/connect');
  return data.authorizationUrl;
}

export async function disconnectGoogleCalendar(): Promise<void> {
  await http.post('/api/calendar/google/disconnect');
  invalidateAllTasksCache();
}

// Triggers the sync job now (returns 202 immediately; the job finishes in the background).
export async function syncGoogleCalendarNow(): Promise<void> {
  await http.post('/api/calendar/google/sync');
  invalidateAllTasksCache();
}
