import { EventTemplate, ReminderAnchor, TaskType, type ReminderDto, type TaskDto } from '../types/task';
import { allDayDays, utcDay } from './schedule';

const pad = (n: number) => String(n).padStart(2, '0');
const shortDate = (d: Date) => d.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
const clock = (d: Date) => `${pad(d.getHours())}:${pad(d.getMinutes())}`;
const sameDay = (a: Date, b: Date) => a.toDateString() === b.toDateString();

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

export const TEMPLATE_LABEL: Record<EventTemplate, string | null> = {
  [EventTemplate.Default]: null,
  [EventTemplate.OutOfOffice]: 'Out of office',
  [EventTemplate.FocusTime]: 'Focus time',
  [EventTemplate.WorkingLocation]: 'Working location',
  [EventTemplate.Birthday]: 'Birthday',
};

export function formatColorKey(key: string): string {
  const text = key.replace(/-/g, ' ');
  return text.charAt(0).toUpperCase() + text.slice(1);
}

const plural = (n: number, unit: string) => `${n} ${unit}${n === 1 ? '' : 's'}`;

export function formatReminder(reminder: ReminderDto, type: TaskType = TaskType.Event): string {
  const fromDeadline = reminder.anchor === ReminderAnchor.Deadline;
  const target = fromDeadline ? 'the deadline' : 'the start';
  const suffix = fromDeadline || type === TaskType.Task ? ` ${target}` : '';

  if (reminder.offsetMinutes !== null) {
    const m = reminder.offsetMinutes;
    if (m === 0) return `At ${target}`;
    if (m % 1440 === 0) return `${plural(m / 1440, 'day')} before${suffix}`;
    if (m % 60 === 0) return `${plural(m / 60, 'hour')} before${suffix}`;
    return `${plural(m, 'minute')} before${suffix}`;
  }
  const days = reminder.daysBefore ?? 0;
  const minuteOfDay = reminder.timeOfDayMinutes ?? 0;
  const time = `${String(Math.floor(minuteOfDay / 60)).padStart(2, '0')}:${String(minuteOfDay % 60).padStart(2, '0')}`;
  const day = days === 0 ? (suffix ? `On the ${fromDeadline ? 'deadline' : 'start'} day` : 'On the day') : `${plural(days, 'day')} before${suffix}`;
  return `${day} at ${time}`;
}

export function splitReason(reasonText: string | null | undefined): { label: string; rest: string } | null {
  if (!reasonText) return null;
  const match = /^(\[[^\]]+\])\s*(.*)$/.exec(reasonText);
  return match ? { label: match[1], rest: match[2] } : { label: '', rest: reasonText };
}
