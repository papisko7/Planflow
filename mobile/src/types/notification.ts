export enum LoginNotificationMode {
  NewDevice = 0,
  EveryLogin = 1,
  Off = 2,
}

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
  incompleteItemsReminder: boolean;
  itemReminders: boolean;
}

export type NotificationSwitch = Exclude<keyof NotificationPreferences, 'loginAlerts' | 'emailEnabled' | 'pushEnabled'>;

export interface TeamNotificationSettings {
  muteNotifications: boolean;
  muteChat: boolean;
}

export enum NotificationEvent {
  TaskAssignedToYou = 10,
  TaskUnassigned = 11,
  TaskChanged = 12,
  TaskDeleted = 13,
  TeamTaskAdded = 14,
  TaskUrgent = 15,
  ItemReminder = 16,
  ParticipantAdded = 17,
  ParticipantRemoved = 18,
  TaskReturnedToPool = 19,
  AddedToTeam = 20,
  TeamRoleChanged = 21,
  RemovedFromTeam = 22,
  TeamDeleted = 23,
  CalendarConnectionLost = 40,
  CalendarItemsIncomplete = 41,
  IncompleteItemsReminder = 42,
}

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
