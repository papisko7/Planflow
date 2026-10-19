import { http } from './httpClient';
import { invalidateAllTasksCache } from './taskCache';

export type GoogleCalendarStatus = {
  connected: boolean;
  email: string | null;
  lastSyncedAtUtc: string | null;
  // False for an account connected before Google Tasks existed (or one that declined that permission): it must reconnect.
  tasksConnected: boolean;
  lastTasksSyncedAtUtc: string | null;
  // True while a sync the user asked for is queued or running on the server.
  syncing: boolean;
};

// `started` is false when the last sync was moments ago, so the server started nothing.
export type GoogleSyncResult = { started: boolean };

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

// Google is fetched only on this call (the server has no timer). It starts the sync of the signed-in user's account and returns at once;
// the work finishes in the background, which the status endpoint reports as `syncing`.
export async function syncGoogleCalendarNow(): Promise<GoogleSyncResult> {
  const { data } = await http.post<GoogleSyncResult>('/api/calendar/google/sync');
  invalidateAllTasksCache();
  return data;
}
