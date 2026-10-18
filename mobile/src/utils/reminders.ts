import { ReminderAnchor, TaskType, type ReminderDto, type ReminderInput } from '../types/task';

// The alerts of an item. The backend (TaskReminders) is the judge; these are its client-side twins so the form can say what is wrong
// before the round trip, and quietly drop the alerts a change of kind makes meaningless.

export const MAX_REMINDERS = 4;
// Same bounds as ItemReminder.MaxOffsetMinutes / MaxDaysBefore.
export const MAX_OFFSET_MINUTES = 4 * 7 * 24 * 60;
export const MAX_DAYS_BEFORE = 28;

// What decides whether an alert fits an item: its kind and the moments it can count back from.
export interface ReminderContext {
  taskType: TaskType;
  allowsAlerts: boolean;
  isAllDay: boolean;
  hasStart: boolean;
  hasDeadline: boolean;
}

export const offsetReminder = (minutes: number, anchor: ReminderAnchor): ReminderInput => ({
  offsetMinutes: minutes,
  daysBefore: null,
  timeOfDayMinutes: null,
  anchor,
});

export const dayReminder = (daysBefore: number, timeOfDayMinutes: number, anchor: ReminderAnchor): ReminderInput => ({
  offsetMinutes: null,
  daysBefore,
  timeOfDayMinutes,
  anchor,
});

// The detail view's reminders as the form edits them (an older server sends no anchor: the start).
export const toReminderInput = (reminder: ReminderDto): ReminderInput => ({
  offsetMinutes: reminder.offsetMinutes,
  daysBefore: reminder.daysBefore,
  timeOfDayMinutes: reminder.timeOfDayMinutes,
  anchor: reminder.anchor ?? ReminderAnchor.Start,
});

export const reminderKey = (r: ReminderInput): string => `${r.anchor}:${r.offsetMinutes ?? ''}:${r.daysBefore ?? ''}:${r.timeOfDayMinutes ?? ''}`;

// "09:30" -> 570; null for anything that is not a time of day.
export function parseTimeOfDay(text: string): number | null {
  const match = /^(\d{1,2}):(\d{2})$/.exec(text.trim());
  if (!match) return null;
  const hours = Number(match[1]);
  const minutes = Number(match[2]);
  return hours < 24 && minutes < 60 ? hours * 60 + minutes : null;
}

export function formatTimeOfDay(minutesOfDay: number): string {
  return `${String(Math.floor(minutesOfDay / 60)).padStart(2, '0')}:${String(minutesOfDay % 60).padStart(2, '0')}`;
}

// Whether one alert can exist on an item of this kind (and has the moment it counts back from).
function fits(reminder: ReminderInput, ctx: ReminderContext): boolean {
  if (!ctx.allowsAlerts) return false;
  if (reminder.anchor === ReminderAnchor.Deadline && ctx.taskType !== TaskType.Task) return false;
  // "Minutes before midnight" is not something a person thinks in: a whole-day item is only reminded in days, at a time of day.
  if (ctx.isAllDay && reminder.offsetMinutes !== null) return false;
  return true;
}

// The alerts that still make sense after the kind / all-day switch changed (the others stay in the form's state, unsent, in case the
// person switches back). Alerts that merely lack their moment are kept: validateReminders names the missing one on save.
export function fitReminders(reminders: ReminderInput[], ctx: ReminderContext): ReminderInput[] {
  return reminders.filter((r) => fits(r, ctx));
}

// null when the list can be saved, otherwise the sentence to show.
export function validateReminders(reminders: ReminderInput[], ctx: ReminderContext): string | null {
  if (reminders.length === 0) return null;
  if (reminders.length > MAX_REMINDERS) return `An item can have at most ${MAX_REMINDERS} alerts.`;
  if (new Set(reminders.map(reminderKey)).size !== reminders.length) return 'Two alerts are the same.';
  for (const reminder of reminders) {
    if (reminder.anchor === ReminderAnchor.Deadline && !ctx.hasDeadline) return 'Set a deadline first: an alert counts back from it.';
    if (reminder.anchor === ReminderAnchor.Start && !ctx.hasStart) return 'Set a start first: an alert counts back from it.';
  }
  return null;
}
