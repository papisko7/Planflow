import { http } from './httpClient';
import { invalidateAllTasksCache } from './taskCache';

export type GoogleCalendarStatus = {
  connected: boolean;
  email: string | null;
  lastSyncedAtUtc: string | null;
  tasksConnected: boolean;
  lastTasksSyncedAtUtc: string | null;
  syncing: boolean;
  // Whether the account allowed PlanFlow to change its events / tasks, i.e. whether an edit made here can be sent to Google.
  // Absent on an older server.
  canWriteCalendar?: boolean;
  canWriteTasks?: boolean;
};

export type GoogleSyncResult = { started: boolean };

export async function getGoogleCalendarStatus(): Promise<GoogleCalendarStatus> {
  const { data } = await http.get<GoogleCalendarStatus>('/api/calendar/google/status');
  return data;
}

export async function getGoogleConnectUrl(): Promise<string> {
  const { data } = await http.get<{ authorizationUrl: string }>('/api/calendar/google/connect');
  return data.authorizationUrl;
}

export async function disconnectGoogleCalendar(): Promise<void> {
  await http.post('/api/calendar/google/disconnect');
  invalidateAllTasksCache();
}

export async function syncGoogleCalendarNow(): Promise<GoogleSyncResult> {
  const { data } = await http.post<GoogleSyncResult>('/api/calendar/google/sync');
  invalidateAllTasksCache();
  return data;
}
