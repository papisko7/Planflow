import { http } from './httpClient';
import type { InboxNotification, NotificationPreferences, TeamNotificationSettings } from '../types/notification';

export async function getNotificationPreferences(): Promise<NotificationPreferences> {
  const { data } = await http.get<NotificationPreferences>('/api/users/me/notification-preferences');
  return data;
}

export async function updateNotificationPreferences(preferences: NotificationPreferences): Promise<NotificationPreferences> {
  const { data } = await http.put<NotificationPreferences>('/api/users/me/notification-preferences', preferences);
  return data;
}

// The caller's own mutes for one team (task e-mails / chat e-mails).
export async function updateTeamNotificationSettings(teamId: string, settings: TeamNotificationSettings): Promise<void> {
  await http.put(`/api/teams/${teamId}/notification-settings`, settings);
}

// ---------- in-app inbox ----------

// Newest first. Pass the createdAtUtc of the last entry you have to load older ones.
export async function getInbox(before?: string, limit = 30): Promise<InboxNotification[]> {
  const { data } = await http.get<InboxNotification[]>('/api/notifications', { params: { before, limit } });
  return data;
}

export async function getUnreadCount(): Promise<number> {
  const { data } = await http.get<{ count: number }>('/api/notifications/unread-count');
  return data.count;
}

export async function markNotificationRead(id: string): Promise<void> {
  await http.post(`/api/notifications/${id}/read`);
}

export async function markAllNotificationsRead(): Promise<void> {
  await http.post('/api/notifications/read-all');
}

// The ticked entries (the server ignores ids that are not yours).
export async function markNotificationsRead(ids: string[]): Promise<void> {
  await http.post('/api/notifications/read', { ids });
}

export async function deleteNotification(id: string): Promise<void> {
  await http.delete(`/api/notifications/${id}`);
}

export async function deleteNotifications(ids: string[]): Promise<void> {
  await http.post('/api/notifications/delete', { ids });
}

// "Clear all": the server keeps an entry whose push has not been delivered yet, so reload the list afterwards.
export async function clearNotifications(): Promise<void> {
  await http.delete('/api/notifications');
}

// ---------- push token of this phone ----------

export async function registerPushToken(token: string, platform: string): Promise<void> {
  await http.put('/api/notifications/push-token', { token, platform });
}

export async function unregisterPushToken(token: string): Promise<void> {
  await http.delete('/api/notifications/push-token', { params: { token } });
}
