import type { Ionicons } from '@expo/vector-icons';
import { colors } from '../theme/theme';
import { NotificationEvent } from '../types/notification';

export interface NotificationIcon {
  name: keyof typeof Ionicons.glyphMap;
  color: string;
}

// One icon per kind of event, so the inbox can be skimmed without reading.
export function notificationIcon(event: NotificationEvent): NotificationIcon {
  switch (event) {
    case NotificationEvent.TaskAssignedToYou:
      return { name: 'person-add-outline', color: colors.primary };
    case NotificationEvent.TaskUnassigned:
      return { name: 'person-remove-outline', color: colors.textMuted };
    case NotificationEvent.TaskChanged:
      return { name: 'create-outline', color: colors.primary };
    case NotificationEvent.TaskDeleted:
      return { name: 'trash-outline', color: colors.danger };
    case NotificationEvent.TeamTaskAdded:
      return { name: 'add-circle-outline', color: colors.primary };
    case NotificationEvent.TaskUrgent:
      return { name: 'flame-outline', color: colors.urgencyCritical };
    case NotificationEvent.ItemReminder:
      return { name: 'alarm-outline', color: colors.primary };
    case NotificationEvent.ParticipantAdded:
      return { name: 'people-circle-outline', color: colors.success };
    case NotificationEvent.ParticipantRemoved:
      return { name: 'people-circle-outline', color: colors.textMuted };
    case NotificationEvent.AddedToTeam:
    case NotificationEvent.TeamRoleChanged:
      return { name: 'people-outline', color: colors.success };
    case NotificationEvent.RemovedFromTeam:
    case NotificationEvent.TeamDeleted:
      return { name: 'people-outline', color: colors.danger };
    case NotificationEvent.CalendarConnectionLost:
    case NotificationEvent.CalendarItemsIncomplete:
      return { name: 'calendar-outline', color: colors.urgencyHigh };
    case NotificationEvent.IncompleteItemsReminder:
      return { name: 'alert-circle-outline', color: colors.urgencyHigh };
    default:
      return { name: 'notifications-outline', color: colors.primary };
  }
}

// "just now", "5 min ago", "3 h ago", "yesterday", then the date - short enough for a list row.
export function timeAgo(iso: string, now: Date = new Date()): string {
  // The server sends UTC without a zone suffix in some serialisations; treat a bare timestamp as UTC.
  const created = new Date(/[zZ]|[+-]\d\d:\d\d$/.test(iso) ? iso : `${iso}Z`);
  const minutes = Math.floor((now.getTime() - created.getTime()) / 60_000);
  if (minutes < 1) return 'just now';
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} h ago`;
  const days = Math.floor(hours / 24);
  if (days === 1) return 'yesterday';
  if (days < 7) return `${days} days ago`;
  return created.toLocaleDateString();
}
