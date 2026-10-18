import { addDays, composeDeadline, startOfDay } from './dateUtils';
import { EventTemplate, TaskType, type ScheduleFields, type TaskDto, type UpdateTaskRequest } from '../types/task';
import { resolveAllDay, resolveBusy, rulesFor, WORKING_LOCATION_PRESETS, type ItemKind } from './templateRules';

// Same bound as the backend's TaskScheduleValidator: one year in hours.
const MAX_HOURS = 366 * 24;
// An event with no end is assumed to last one hour (CalendarItemUrgencyScoreFactory.DefaultEventDurationHours).
export const DEFAULT_EVENT_HOURS = 1;
const HOUR_MS = 3_600_000;

export function emptySchedule(taskType: TaskType = TaskType.Task, eventTemplate: EventTemplate = EventTemplate.Default): ScheduleFields {
  return {
    taskType,
    eventTemplate,
    isAllDay: resolveAllDay(taskType, eventTemplate, false),
    location: rulesFor(taskType, eventTemplate).location === 'workingLocation' ? WORKING_LOCATION_PRESETS[0] : null,
    colorKey: null,
    isBusy: true,
    deadlineUtc: null,
    startUtc: null,
    endUtc: null,
    estimatedEffortHours: null,
    isRecurring: false,
    recurrenceIntervalHours: null,
    recurrenceEndsAtUtc: null,
  };
}

// The stored values as they are (the wire convention for all-day items). Quick actions resend these untouched; the edit
// form starts from scheduleForEditing instead.
export function scheduleFromTask(task: TaskDto): ScheduleFields {
  return {
    taskType: task.taskType,
    eventTemplate: task.eventTemplate,
    isAllDay: task.isAllDay,
    location: task.location,
    colorKey: task.colorKey,
    isBusy: task.isBusy,
    deadlineUtc: task.deadlineUtc,
    startUtc: task.startUtc,
    endUtc: task.endUtc,
    estimatedEffortHours: task.estimatedEffortHours,
    isRecurring: task.isRecurring,
    recurrenceIntervalHours: task.recurrenceIntervalHours,
    recurrenceEndsAtUtc: task.recurrenceEndsAtUtc,
  };
}

// What the edit form starts from. In the form an all-day event is what the date pickers produce - local 00:00 of the first
// day to local 23:59 of the last - whichever way it is stored (the flag convention, or the older shape-only one);
// scheduleForRequest turns it back into the stored convention on save. The form always shows both ends of a timed event, so
// one stored without an end (e.g. imported) is opened with the end the backend assumes anyway.
export function scheduleForEditing(task: TaskDto): ScheduleFields {
  const schedule = scheduleFromTask(task);
  if (task.taskType !== TaskType.Event || !schedule.startUtc) return schedule;
  const days = allDayDays(task);
  if (days) {
    return { ...schedule, isAllDay: true, startUtc: composeDeadline(days.first, '00:00'), endUtc: composeDeadline(days.last, null) };
  }
  return schedule.endUtc ? schedule : { ...schedule, ...pairEventTimes(schedule.startUtc, null, 'start') };
}

// Picking another chip of the form (Task, Event, Out of office, ...): the type and template change, what the new kind does
// not have is cleared, and the dates are re-shaped when the kind is all-day (Birthday) or never all-day (Focus time).
export function switchKind(s: ScheduleFields, kind: ItemKind): ScheduleFields {
  const rules = rulesFor(kind.taskType, kind.template);
  const wasAllDay = s.taskType === TaskType.Event && s.isAllDay;
  const next: ScheduleFields = { ...s, taskType: kind.taskType, eventTemplate: kind.template };

  // The place changes meaning between a free-text place and a working-location label, so it does not carry over.
  const mode = rulesFor(s.taskType, s.eventTemplate).location;
  if (rules.location !== mode) next.location = rules.location === 'workingLocation' ? WORKING_LOCATION_PRESETS[0] : null;
  if (!rules.allowsColor) next.colorKey = null;
  next.isBusy = resolveBusy(kind.taskType, kind.template, s.isBusy);

  next.isAllDay = resolveAllDay(kind.taskType, kind.template, wasAllDay);
  if (kind.taskType === TaskType.Event) {
    if (next.isAllDay && !wasAllDay) Object.assign(next, pairAllDayDates(s.startUtc, s.endUtc, s.startUtc ? 'start' : 'end'));
    if (!next.isAllDay && wasAllDay) Object.assign(next, allDayToTimed(s.startUtc));
  }
  return next;
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

// An all-day event covers whole local days: it starts at 00:00 of the first day and ends at 23:59 of the last
// (the same "end of day" an all-day deadline uses). There is no separate flag in the API - the shape IS the flag.
export function isAllDayRange(startUtc: string | null, endUtc: string | null): boolean {
  if (!startUtc || !endUtc) return false;
  const start = new Date(startUtc);
  const end = new Date(endUtc);
  return start.getHours() === 0 && start.getMinutes() === 0 && end.getHours() === 23 && end.getMinutes() === 59 && end >= start;
}

// The calendar DATE inside a UTC timestamp, as a local Date at midnight of that date. An imported all-day item stores
// its dates as midnight UTC and means "that date, wherever you are", so it is read from the UTC parts: converting to
// local time first would show the previous day in any zone west of UTC.
export function utcDay(iso: string): Date {
  const d = new Date(iso);
  return new Date(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate());
}

// The first and last calendar day of an all-day event, or null when the event is not all-day. Two storage conventions meet here:
// - imported (isAllDay): dates as midnight UTC, the end being the midnight AFTER the last day (Google's convention);
// - created in the app before the form learnt the flag: local 00:00 to local 23:59, recognised by that shape alone.
export function allDayDays(item: {
  startUtc: string | null;
  endUtc: string | null;
  isAllDay?: boolean;
}): { first: Date; last: Date } | null {
  if (!item.startUtc) return null;
  if (item.isAllDay) {
    const first = utcDay(item.startUtc);
    const last = item.endUtc ? addDays(utcDay(item.endUtc), -1) : first;
    return { first, last: last < first ? first : last };
  }
  if (isAllDayRange(item.startUtc, item.endUtc)) {
    return { first: startOfDay(new Date(item.startUtc)), last: startOfDay(new Date(item.endUtc!)) };
  }
  return null;
}

// All-day twin of pairEventTimes, working on whole days. Start is the base value: picking a start day makes the
// event last that one day (end = 23:59 of it), and an end that is still on or after the start day is kept, so a
// multi-day event the user set up is not collapsed. Picking an end day first fills the start with that same day.
export function pairAllDayDates(
  startUtc: string | null,
  endUtc: string | null,
  changed: 'start' | 'end',
): { startUtc: string | null; endUtc: string | null } {
  let startDay = startUtc ? startOfDay(new Date(startUtc)) : null;
  let endDay = endUtc ? startOfDay(new Date(endUtc)) : null;
  if (changed === 'start' && startDay && (!endDay || endDay < startDay)) endDay = startDay;
  if (changed === 'end' && endDay && (!startDay || startDay > endDay)) startDay = endDay;
  return {
    startUtc: startDay ? composeDeadline(startDay, '00:00') : null,
    endUtc: endDay ? composeDeadline(endDay, null) : null,
  };
}

// Leaving all-day mode: the first day becomes a normal 09:00-10:00 slot (the picker's default time) to adjust.
export function allDayToTimed(startUtc: string | null): { startUtc: string | null; endUtc: string | null } {
  if (!startUtc) return { startUtc: null, endUtc: null };
  return pairEventTimes(composeDeadline(startOfDay(new Date(startUtc)), '09:00'), null, 'start');
}

// The moment a person plans around: a Task's deadline, an Event's start (mirrors CalendarItem.DueUtc).
export function dueUtc(item: Pick<TaskDto, 'taskType' | 'deadlineUtc' | 'startUtc'>): string | null {
  return item.taskType === TaskType.Event ? item.startUtc : item.deadlineUtc;
}

// An Event whose end (or start + 1h when it has no end) has passed is history: the backend scores it 0 and it
// never counts as open work. A Task is never "past" - an overdue Task stays urgent.
export function isPastEvent(
  item: Pick<TaskDto, 'taskType' | 'startUtc' | 'endUtc'> & { isAllDay?: boolean },
  now: Date = new Date(),
): boolean {
  if (item.taskType !== TaskType.Event || !item.startUtc) return false;
  // An imported all-day event is over when its last day is over for the person looking at it (local midnight after that day),
  // not at midnight UTC, which for a viewer in Poland is already two in the morning of the next day.
  const days = item.isAllDay ? allDayDays(item) : null;
  if (days) return addDays(days.last, 1).getTime() <= now.getTime();
  const end = item.endUtc ? new Date(item.endUtc).getTime() : new Date(item.startUtc).getTime() + DEFAULT_EVENT_HOURS * 3_600_000;
  return end < now.getTime();
}

// Drops what does not apply to the chosen type and template, so switching kinds in the form never sends stale values
// (the backend does the same; this keeps the request honest).
export function normalizeSchedule(s: ScheduleFields): ScheduleFields {
  const isEvent = s.taskType === TaskType.Event;
  const eventTemplate = isEvent ? s.eventTemplate : EventTemplate.Default;
  const rules = rulesFor(s.taskType, eventTemplate);
  return {
    ...s,
    eventTemplate,
    isAllDay: resolveAllDay(s.taskType, eventTemplate, s.isAllDay),
    location: rules.location === 'none' ? null : s.location?.trim() || null,
    colorKey: rules.allowsColor ? s.colorKey : null,
    isBusy: resolveBusy(s.taskType, eventTemplate, s.isBusy),
    deadlineUtc: isEvent ? null : s.deadlineUtc,
    startUtc: isEvent ? s.startUtc : null,
    endUtc: isEvent ? s.endUtc : null,
    recurrenceIntervalHours: s.isRecurring ? s.recurrenceIntervalHours : null,
    recurrenceEndsAtUtc: s.isRecurring ? s.recurrenceEndsAtUtc : null,
  };
}

// A calendar day as midnight UTC - the stored form of an all-day date (see utcDay for the way back).
const utcMidnight = (d: Date) => new Date(Date.UTC(d.getFullYear(), d.getMonth(), d.getDate())).toISOString();

// The fields as the API stores them. Timed values pass through; an all-day event's local days (start 00:00, end 23:59) become
// midnight UTC of the first day and of the day AFTER the last one (Google's exclusive end), so the day never shifts with the
// time zone. Use this for the request, normalizeSchedule + validateSchedule for what the user sees in the form.
export function scheduleForRequest(raw: ScheduleFields): ScheduleFields {
  const s = normalizeSchedule(raw);
  if (!s.isAllDay || !s.startUtc) return s;
  const first = startOfDay(new Date(s.startUtc));
  const last = s.endUtc ? startOfDay(new Date(s.endUtc)) : first;
  return { ...s, startUtc: utcMidnight(first), endUtc: utcMidnight(addDays(last < first ? first : last, 1)) };
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
