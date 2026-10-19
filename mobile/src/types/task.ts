// Mirrors backend PlanFlow.Domain.Enums.TaskStatus — no JsonStringEnumConverter is configured,
// so the API serializes this as a plain int; the numbers here must stay in sync. 2 (the old "Blocked")
// is retired: "blocked" is now derived from blockedByTaskId.
export enum TaskStatus {
  Backlog = 0,
  InProgress = 1,
  Completed = 3,
  Cancelled = 4,
  Confirmed = 5,
  Tentative = 6,
}

export enum TaskType {
  Task = 0,
  Event = 1,
}

export enum EventTemplate {
  Default = 0,
  OutOfOffice = 1,
  FocusTime = 2,
  WorkingLocation = 3,
  Birthday = 4,
}

export enum RecurrenceFrequency {
  Daily = 0,
  Weekly = 1,
  Monthly = 2,
  Yearly = 3,
}

export enum SeriesScope {
  This = 0,
  ThisAndFollowing = 1,
  All = 2,
}

export enum ReminderAnchor {
  Start = 0,
  Deadline = 1,
}

// Mirrors PlanFlow.Application.Tasks.Common.ReminderDto. Offset form: offsetMinutes before the anchor.
// Day form: daysBefore whole days earlier, at timeOfDayMinutes after midnight. The fields of the other form are null.
export interface ReminderDto {
  offsetMinutes: number | null;
  daysBefore: number | null;
  timeOfDayMinutes: number | null;
  anchor?: ReminderAnchor; // absent on older servers: the start
}

export interface ReminderInput {
  offsetMinutes: number | null;
  daysBefore: number | null;
  timeOfDayMinutes: number | null;
  anchor: ReminderAnchor;
}

export interface TaskDto {
  id: string;
  teamId: string;
  assignedUserId: string | null;
  title: string;
  description: string | null;
  status: TaskStatus;
  deadlineUtc: string | null;
  impactScore: number | null;
  blockedByTaskId: string | null;
  manualUrgencyOverride: number | null;
  manualUrgencyOverrideExpiresAtUtc: string | null;
  currentUrgencyScore: number;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  taskType: TaskType;
  startUtc: string | null; // Event only
  endUtc: string | null; // Event only
  estimatedEffortHours: number | null;
  // The app's own repeat rule: every `recurrenceInterval` days / weeks / months / years; for weekly `recurrenceWeekdays` is a bit mask
  // (Mon = 1 ... Sun = 64, see utils/recurrence.ts). All null for an item that does not repeat or whose series is Google's.
  isRecurring: boolean;
  recurrenceFrequency: RecurrenceFrequency | null;
  recurrenceInterval: number | null;
  recurrenceWeekdays: number | null;
  recurrenceEndsAtUtc: string | null;
  useAiEnhancement: boolean;
  aiImpactScore: number | null;
  aiImpactReasoning: string | null;
  isDataIncomplete: boolean;
  eventTemplate: EventTemplate;
  location: string | null;
  colorKey: string | null; // palette name (e.g. "tomato"), null = default colour
  isBusy: boolean;
  // All-day items store their DATES as midnight UTC, read literally (see utils/schedule.ts allDayDays); the end is the
  // midnight after the last day. Items created in the app earlier mark all-day by shape instead and have this false.
  isAllDay: boolean;
  externalSettings: string | null; // read-only note about Google-only settings of an imported Out of office / Focus time
  isExternal: boolean;
  isDeadlineDefaulted: boolean;
  googleTaskListId: string | null;
  googleTaskListTitle: string | null;
  participantUserIds: string[];
  // The occurrences of one repeating item share a series id (an own series' id, or Google's recurringEventId); null = not part of a series.
  seriesId: string | null;
  itemListId: string | null;
  isAssignmentLocked?: boolean;
  assignedByUserId?: string | null;
  reasonText?: string | null;
  // Client side only, set by utils/seriesGrouping when a list shows one entry for a whole series: how many other occurrences it hides.
  seriesMore?: number;
}

export interface UrgencyScoreBreakdownDto {
  deadlineComponent: number;
  blockingComponent: number;
  impactComponent: number | null; // null = task not rated, its weight is shared by the other factors
  userOverrideComponent: number;
  scheduledComponent?: number | null;
  algorithmVersion?: number;
  finalScore: number;
  isBlocked: boolean; // true = waiting on an unfinished blocker, score is halved
  createdAtUtc: string;
  reasonText?: string | null; // null on score rows written before the explanation existed
}

export interface TaskDetailDto {
  task: TaskDto;
  latestScoreBreakdown: UrgencyScoreBreakdownDto | null;
  reminders?: ReminderDto[]; // the item's alerts (imported from Google for an event of Google's); absent on older servers
}

export interface ScheduleFields {
  taskType: TaskType;
  eventTemplate: EventTemplate;
  isAllDay: boolean; // Event only; the dates then are whole days (see utils/schedule.ts toWireSchedule)
  location: string | null;
  colorKey: string | null;
  isBusy: boolean;
  deadlineUtc: string | null; // Task only
  startUtc: string | null; // Event only
  endUtc: string | null; // Event only
  estimatedEffortHours: number | null;
  isRecurring: boolean;
  recurrenceFrequency: RecurrenceFrequency | null;
  recurrenceInterval: number | null;
  recurrenceWeekdays: number | null;
  recurrenceEndsAtUtc: string | null;
}

export interface CreateTaskRequest extends ScheduleFields {
  title: string;
  description: string | null;
  assignedUserId: string | null;
  impactScore: number | null;
  blockedByTaskId: string | null;
  reminders?: ReminderInput[];
  participantUserIds?: string[];
  itemListId?: string | null;
}

export interface UpdateTaskRequest extends ScheduleFields {
  title: string;
  description: string | null;
  status: TaskStatus;
  assignedUserId: string | null;
  impactScore: number | null;
  blockedByTaskId: string | null;
  manualUrgencyOverride: number | null;
  manualUrgencyOverrideExpiresAtUtc: string | null;
  useAiEnhancement: boolean;
  // Both lists: left out = the server keeps what it has (quick actions send neither); a list - even an empty one - replaces it.
  reminders?: ReminderInput[];
  participantUserIds?: string[];
  // The whole value, not "left out = unchanged": null takes the item out of its list, so every update resends the stored one.
  itemListId?: string | null;
  // Which occurrences a change of a repeating item reaches; left out = only this one. The repeat rule fields count only for the other two.
  scope?: SeriesScope;
}

export type TaskWithTeam = TaskDto & { teamName: string };
