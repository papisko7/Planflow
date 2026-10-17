import { TaskType, type TaskDto } from '../types/task';
import { formatDuration, INTERVAL_UNITS } from './duration';
import { isAllDayRange } from './schedule';

const pad = (n: number) => String(n).padStart(2, '0');
const shortDate = (d: Date) => d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
const clock = (d: Date) => `${pad(d.getHours())}:${pad(d.getMinutes())}`;
const sameDay = (a: Date, b: Date) => a.toDateString() === b.toDateString();

// Where an item sits in time, for cards: a Task shows its deadline day, an Event shows its range
// ("Oct 5 · 14:00-15:00", "Oct 5 · all day", or "Oct 5 14:00 - Oct 6 10:00" when it spans days).
export function formatWhen(item: Pick<TaskDto, 'taskType' | 'deadlineUtc' | 'startUtc' | 'endUtc'>): string | null {
  if (item.taskType !== TaskType.Event) {
    return item.deadlineUtc ? shortDate(new Date(item.deadlineUtc)) : null;
  }
  if (!item.startUtc) return null;
  const start = new Date(item.startUtc);
  if (!item.endUtc) return `${shortDate(start)} · ${clock(start)}`;
  const end = new Date(item.endUtc);
  if (isAllDayRange(item.startUtc, item.endUtc)) {
    return sameDay(start, end) ? `${shortDate(start)} · all day` : `${shortDate(start)} - ${shortDate(end)} · all day`;
  }
  return sameDay(start, end)
    ? `${shortDate(start)} · ${clock(start)}-${clock(end)}`
    : `${shortDate(start)} ${clock(start)} - ${shortDate(end)} ${clock(end)}`;
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
