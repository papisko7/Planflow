// Mirrors backend PlanFlow.Domain.Enums.TaskStatus — no JsonStringEnumConverter is configured,
// so the API serializes this as a plain int; the numbers here must stay in sync. 2 (the old "Blocked")
// is retired: "blocked" is now derived from blockedByTaskId.
export enum TaskStatus {
  // Task statuses
  Backlog = 0,
  InProgress = 1,
  Completed = 3,
  // Both types
  Cancelled = 4,
  // Event statuses
  Confirmed = 5,
  Tentative = 6,
}

// Mirrors backend PlanFlow.Domain.Enums.TaskType (plain int on the wire): a Task is work with a deadline,
// an Event happens in a time range.
export enum TaskType {
  Task = 0,
  Event = 1,
}

// Mirrors backend PlanFlow.Domain.Enums.EventTemplate (plain int on the wire): the Google-Calendar-style kind of an Event.
// A Task is always Default.
export enum EventTemplate {
  Default = 0,
  OutOfOffice = 1,
  FocusTime = 2,
  WorkingLocation = 3,
  Birthday = 4,
}

// Mirrors backend PlanFlow.Domain.Enums.ReminderAnchor (plain int on the wire): what a reminder counts back from.
// An event's reminders always count from its start; a task may use its planned start or its deadline.
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

// Mirrors PlanFlow.Application.Tasks.Common.ReminderInput: one alert as the form sends it (the same shape as ReminderDto, anchor always set).
export interface ReminderInput {
  offsetMinutes: number | null;
  daysBefore: number | null;
  timeOfDayMinutes: number | null;
  anchor: ReminderAnchor;
}

// Mirrors PlanFlow.Application.Tasks.Common.TaskDto
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
  isRecurring: boolean;
  recurrenceIntervalHours: number | null;
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
  // midnight after the last day. Items created in the app before 7.5.5 mark all-day by shape instead and have this false.
  isAllDay: boolean;
  externalSettings: string | null; // read-only note about Google-only settings of an imported Out of office / Focus time
  // Imported from Google Calendar: Google owns its title, time, place, description, colour and template (the server ignores
  // edits to them), so the edit form shows those read-only.
  isExternal: boolean;
  // A Google task with no due day in Google: its deadline is a placeholder ("tomorrow") that the user may still set.
  isDeadlineDefaulted: boolean;
  // The Google Tasks list an imported task belongs to (the Items tab groups such tasks by it); null for everything else.
  googleTaskListId: string | null;
  googleTaskListTitle: string | null;
  // The guests of an event / co-contractors of a task (not the assignee), as user ids. Empty for older servers' rows.
  participantUserIds: string[];
  // Latest "[Priority] why" sentence; only the team list endpoint fills it.
  reasonText?: string | null;
}

// Mirrors PlanFlow.Application.Tasks.Common.UrgencyScoreBreakdownDto
export interface UrgencyScoreBreakdownDto {
  deadlineComponent: number;
  blockingComponent: number;
  impactComponent: number | null; // null = task not rated, its weight is shared by the other factors
  userOverrideComponent: number;
  // Algorithm v2: closeness of a task's planned start; null when it did not count, absent on rows written by v1.
  scheduledComponent?: number | null;
  algorithmVersion?: number;
  finalScore: number;
  isBlocked: boolean; // true = waiting on an unfinished blocker, score is halved
  createdAtUtc: string;
  reasonText?: string | null; // null on score rows written before the explanation existed
}

// Mirrors PlanFlow.Application.Tasks.Common.TaskDetailDto
export interface TaskDetailDto {
  task: TaskDto;
  latestScoreBreakdown: UrgencyScoreBreakdownDto | null;
  reminders?: ReminderDto[]; // the item's alerts (imported from Google for an event of Google's); absent on older servers
}

// The scheduling and template fields shared by the create and update requests (type, template, dates, effort, recurrence,
// place, colour, busy/free). Which of them an item has depends on its type and template (utils/templateRules.ts).
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
  recurrenceIntervalHours: number | null;
  recurrenceEndsAtUtc: string | null;
}

// Mirrors PlanFlow.Api.Contracts.CreateTaskRequest
export interface CreateTaskRequest extends ScheduleFields {
  title: string;
  description: string | null;
  assignedUserId: string | null;
  impactScore: number | null;
  blockedByTaskId: string | null;
  reminders?: ReminderInput[];
  participantUserIds?: string[];
}

// Mirrors PlanFlow.Api.Contracts.UpdateTaskRequest
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
}

// A task plus the name of its team (resolved client-side from the teams list) for display.
export type TaskWithTeam = TaskDto & { teamName: string };
