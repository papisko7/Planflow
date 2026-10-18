import { EventTemplate, TaskType, type ReminderDto, type TaskDto } from '../types/task';
import { formatDuration, INTERVAL_UNITS } from './duration';
import { allDayDays, utcDay } from './schedule';

const pad = (n: number) => String(n).padStart(2, '0');
const shortDate = (d: Date) => d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
const clock = (d: Date) => `${pad(d.getHours())}:${pad(d.getMinutes())}`;
const sameDay = (a: Date, b: Date) => a.toDateString() === b.toDateString();

// Where an item sits in time, for cards: a Task shows its deadline day (a whole-day one - e.g. from Google Tasks - is read as the date it
// stores, wherever the viewer is), an Event shows its range
// ("Oct 5 · 14:00-15:00", "Oct 5 · all day", or "Oct 5 14:00 - Oct 6 10:00" when it spans days).
export function formatWhen(
  item: Pick<TaskDto, 'taskType' | 'deadlineUtc' | 'startUtc' | 'endUtc'> & { isAllDay?: boolean },
): string | null {
  if (item.taskType !== TaskType.Event) {
    if (!item.deadlineUtc) return null;
    return shortDate(item.isAllDay ? utcDay(item.deadlineUtc) : new Date(item.deadlineUtc));
  }
  if (!item.startUtc) return null;
  const days = allDayDays(item);
  if (days) {
    return sameDay(days.first, days.last)
      ? `${shortDate(days.first)} · all day`
      : `${shortDate(days.first)} - ${shortDate(days.last)} · all day`;
  }
  const start = new Date(item.startUtc);
  if (!item.endUtc) return `${shortDate(start)} · ${clock(start)}`;
  const end = new Date(item.endUtc);
  return sameDay(start, end)
    ? `${shortDate(start)} · ${clock(start)}-${clock(end)}`
    : `${shortDate(start)} ${clock(start)} - ${shortDate(end)} ${clock(end)}`;
}

// The Google-Calendar-style kind of an event, for a badge; null for an ordinary event or a task.
export const TEMPLATE_LABEL: Record<EventTemplate, string | null> = {
  [EventTemplate.Default]: null,
  [EventTemplate.OutOfOffice]: 'Out of office',
  [EventTemplate.FocusTime]: 'Focus time',
  [EventTemplate.WorkingLocation]: 'Working location',
  [EventTemplate.Birthday]: 'Birthday',
};

// "palette-name" -> "Palette name" (the real colour swatches arrive with the palette in the form).
export function formatColorKey(key: string): string {
  const text = key.replace(/-/g, ' ');
  return text.charAt(0).toUpperCase() + text.slice(1);
}

const plural = (n: number, unit: string) => `${n} ${unit}${n === 1 ? '' : 's'}`;

// "10 minutes before" / "1 day before at 09:00" / "On the day at 09:00".
export function formatReminder(reminder: ReminderDto): string {
  if (reminder.offsetMinutes !== null) {
    const m = reminder.offsetMinutes;
    if (m === 0) return 'At the start';
    if (m % 1440 === 0) return `${plural(m / 1440, 'day')} before`;
    if (m % 60 === 0) return `${plural(m / 60, 'hour')} before`;
    return `${plural(m, 'minute')} before`;
  }
  const days = reminder.daysBefore ?? 0;
  const minuteOfDay = reminder.timeOfDayMinutes ?? 0;
  const time = `${String(Math.floor(minuteOfDay / 60)).padStart(2, '0')}:${String(minuteOfDay % 60).padStart(2, '0')}`;
  return `${days === 0 ? 'On the day' : `${plural(days, 'day')} before`} at ${time}`;
}

// "every 2 d" / null when the item does not repeat.
export function formatRecurrence(item: Pick<TaskDto, 'isRecurring' | 'recurrenceIntervalHours'>): string | null {
  if (!item.isRecurring || item.recurrenceIntervalHours === null) return null;
  return `every ${formatDuration(item.recurrenceIntervalHours, INTERVAL_UNITS)}`;
}

// The backend writes "[High] due in 2d, impact 8/10" - the bracketed priority first, the explanation after it.
export function splitReason(reasonText: string | null | undefined): { label: string; rest: string } | null {
  if (!reasonText) return null;
  const match = /^(\[[^\]]+\])\s*(.*)$/.exec(reasonText);
  return match ? { label: match[1], rest: match[2] } : { label: '', rest: reasonText };
}
