import { TaskType, type ScheduleFields, type TaskDto, type UpdateTaskRequest } from '../types/task';

// Same bound as the backend's TaskScheduleValidator: one year in hours.
const MAX_HOURS = 366 * 24;
// An event with no end is assumed to last one hour (CalendarItemUrgencyScoreFactory.DefaultEventDurationHours).
export const DEFAULT_EVENT_HOURS = 1;
const HOUR_MS = 3_600_000;

export function emptySchedule(taskType: TaskType = TaskType.Task): ScheduleFields {
  return {
    taskType,
    deadlineUtc: null,
    startUtc: null,
    endUtc: null,
    estimatedEffortHours: null,
    isRecurring: false,
    recurrenceIntervalHours: null,
    recurrenceEndsAtUtc: null,
  };
}

export function scheduleFromTask(task: TaskDto): ScheduleFields {
  return {
    taskType: task.taskType,
    deadlineUtc: task.deadlineUtc,
    startUtc: task.startUtc,
    endUtc: task.endUtc,
    estimatedEffortHours: task.estimatedEffortHours,
    isRecurring: task.isRecurring,
    recurrenceIntervalHours: task.recurrenceIntervalHours,
    recurrenceEndsAtUtc: task.recurrenceEndsAtUtc,
  };
}

// The form always shows both ends of an event, so an event stored without an end (e.g. imported) is opened with the
// end the backend assumes anyway. Only the edit form uses this; quick actions resend the stored values untouched.
export function scheduleForEditing(task: TaskDto): ScheduleFields {
  const schedule = scheduleFromTask(task);
  return task.taskType === TaskType.Event && schedule.startUtc && !schedule.endUtc
    ? { ...schedule, ...pairEventTimes(schedule.startUtc, null, 'start') }
    : schedule;
}

// Start and end fill each other in so the user always sees a complete range after touching either one:
// a new start gives end = start + 1h, a new end gives start = end - 1h (the time of day of the picked one is kept,
// so a day picked with the default 09:00 gives 09:00-10:00 / 08:00-09:00). The other side is left alone when it
// is already consistent (start before end), so a range the user adjusted is not overwritten.
export function pairEventTimes(
  startUtc: string | null,
  endUtc: string | null,
  changed: 'start' | 'end',
): { startUtc: string | null; endUtc: string | null } {
  const start = startUtc ? new Date(startUtc).getTime() : null;
  const end = endUtc ? new Date(endUtc).getTime() : null;
  if (changed === 'start' && start !== null && (end === null || end <= start)) {
    return { startUtc, endUtc: new Date(start + DEFAULT_EVENT_HOURS * HOUR_MS).toISOString() };
  }
  if (changed === 'end' && end !== null && (start === null || start >= end)) {
    return { startUtc: new Date(end - DEFAULT_EVENT_HOURS * HOUR_MS).toISOString(), endUtc };
  }
  return { startUtc, endUtc };
}

// The moment a person plans around: a Task's deadline, an Event's start (mirrors CalendarItem.DueUtc).
export function dueUtc(item: Pick<TaskDto, 'taskType' | 'deadlineUtc' | 'startUtc'>): string | null {
  return item.taskType === TaskType.Event ? item.startUtc : item.deadlineUtc;
}

// An Event whose end (or start + 1h when it has no end) has passed is history: the backend scores it 0 and it
// never counts as open work. A Task is never "past" - an overdue Task stays urgent.
export function isPastEvent(item: Pick<TaskDto, 'taskType' | 'startUtc' | 'endUtc'>, now: Date = new Date()): boolean {
  if (item.taskType !== TaskType.Event || !item.startUtc) return false;
  const end = item.endUtc ? new Date(item.endUtc).getTime() : new Date(item.startUtc).getTime() + DEFAULT_EVENT_HOURS * 3_600_000;
  return end < now.getTime();
}

// Drops what does not apply to the chosen type, so switching Task <-> Event in the form never sends stale dates
// (the backend does the same; this keeps the request honest).
export function normalizeSchedule(s: ScheduleFields): ScheduleFields {
  const isEvent = s.taskType === TaskType.Event;
  return {
    ...s,
    deadlineUtc: isEvent ? null : s.deadlineUtc,
    startUtc: isEvent ? s.startUtc : null,
    endUtc: isEvent ? s.endUtc : null,
    recurrenceIntervalHours: s.isRecurring ? s.recurrenceIntervalHours : null,
    recurrenceEndsAtUtc: s.isRecurring ? s.recurrenceEndsAtUtc : null,
  };
}

// Client-side twin of the backend validator, so the user gets a message before the round trip.
// `isNew` adds the create-only rules (a new item may not already be in the past).
export function validateSchedule(raw: ScheduleFields, isNew: boolean): string | null {
  const s = normalizeSchedule(raw);
  const isEvent = s.taskType === TaskType.Event;
  const now = Date.now();

  if (s.estimatedEffortHours !== null && !(s.estimatedEffortHours > 0 && s.estimatedEffortHours <= MAX_HOURS)) {
    return 'Effort must be greater than 0 and at most one year.';
  }

  if (isEvent) {
    if (!s.startUtc) return 'An event needs a start time.';
    const start = new Date(s.startUtc).getTime();
    if (s.endUtc && new Date(s.endUtc).getTime() < start) return 'The end must not be earlier than the start.';
    const end = s.endUtc ? new Date(s.endUtc).getTime() : start + DEFAULT_EVENT_HOURS * 3_600_000;
    if (isNew && end <= now) return 'The event must not be already over.';
  } else if (isNew && s.deadlineUtc && new Date(s.deadlineUtc).getTime() <= now) {
    return 'Deadline must be in the future.';
  }

  if (s.isRecurring) {
    if (s.recurrenceIntervalHours === null || s.recurrenceIntervalHours < 1 || s.recurrenceIntervalHours > MAX_HOURS) {
      return 'Set how often it repeats (at least 1 hour, at most one year).';
    }
    const anchor = dueUtc(s);
    if (!anchor) return isEvent ? 'A repeating event needs a start.' : 'A repeating task needs a deadline to repeat from.';
    if (s.recurrenceEndsAtUtc && new Date(s.recurrenceEndsAtUtc).getTime() <= new Date(anchor).getTime()) {
      return 'The repeat must end after the first occurrence.';
    }
  }
  return null;
}

// The update endpoint replaces the whole item, so quick actions (done, claim, ...) resend every field of the
// existing one; `overrides` carries the single thing that changes.
export function toUpdateRequest(task: TaskDto, overrides: Partial<UpdateTaskRequest> = {}): UpdateTaskRequest {
  return {
    title: task.title,
    description: task.description,
    status: task.status,
    assignedUserId: task.assignedUserId,
    impactScore: task.impactScore,
    blockedByTaskId: task.blockedByTaskId,
    manualUrgencyOverride: task.manualUrgencyOverride,
    manualUrgencyOverrideExpiresAtUtc: task.manualUrgencyOverrideExpiresAtUtc,
    useAiEnhancement: task.useAiEnhancement, // not editable in the app yet, but the backend resets it when omitted
    ...scheduleFromTask(task),
    ...overrides,
  };
}
