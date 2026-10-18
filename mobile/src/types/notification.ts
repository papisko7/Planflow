// Mirrors PlanFlow.Domain.Enums.LoginNotificationMode (the API serialises enums as numbers).
export enum LoginNotificationMode {
  NewDevice = 0,
  EveryLogin = 1,
  Off = 2,
}

// Mirrors PlanFlow.Application.Users.Common.NotificationPreferencesDto
export interface NotificationPreferences {
  emailEnabled: boolean;
  pushEnabled: boolean;
  loginAlerts: LoginNotificationMode;
  taskAssignments: boolean;
  taskChanges: boolean;
  teamTasks: boolean;
  taskUrgency: boolean;
  teamMembership: boolean;
  chatDigest: boolean;
  calendarIssues: boolean;
}

// The on/off switches (everything except loginAlerts), in the order they are shown.
export type NotificationSwitch = Exclude<keyof NotificationPreferences, 'loginAlerts' | 'emailEnabled' | 'pushEnabled'>;

// Mirrors PlanFlow.Api.Contracts.TeamNotificationSettingsRequest
export interface TeamNotificationSettings {
  muteNotifications: boolean;
  muteChat: boolean;
}

// Mirrors PlanFlow.Domain.Enums.NotificationEvent (the API serialises enums as numbers). Only the ones that reach the inbox matter here.
export enum NotificationEvent {
  TaskAssignedToYou = 10,
  TaskUnassigned = 11,
  TaskChanged = 12,
  TaskDeleted = 13,
  TeamTaskAdded = 14,
  TaskUrgent = 15,
  AddedToTeam = 20,
  TeamRoleChanged = 21,
  RemovedFromTeam = 22,
  TeamDeleted = 23,
  CalendarConnectionLost = 40,
  CalendarItemsIncomplete = 41,
}

// Mirrors PlanFlow.Application.Notifications.Inbox.NotificationDto
export interface InboxNotification {
  id: string;
  event: NotificationEvent;
  title: string;
  body: string;
  createdAtUtc: string;
  isRead: boolean;
  teamId: string | null;
  taskId: string | null;
}
