import { RecurrenceFrequency, type ScheduleFields, type TaskDto } from '../types/task';

// Same bound as the backend (RecurrenceRules.MaxInterval): "every 1 ... 99" days / weeks / months / years.
export const MAX_INTERVAL = 99;

// The days of a week as bits, Monday first (the backend's mask): Mon = 1, Tue = 2, ... Sun = 64.
export const WEEKDAYS = [
  { bit: 1, short: 'Mon' },
  { bit: 2, short: 'Tue' },
  { bit: 4, short: 'Wed' },
  { bit: 8, short: 'Thu' },
  { bit: 16, short: 'Fri' },
  { bit: 32, short: 'Sat' },
  { bit: 64, short: 'Sun' },
] as const;

// The bit of the weekday a date falls on (JavaScript counts Sunday as 0).
export const weekdayBit = (date: Date): number => 1 << ((date.getDay() + 6) % 7);

export const FREQUENCY_LABEL: Record<RecurrenceFrequency, { chip: string; unit: string }> = {
  [RecurrenceFrequency.Daily]: { chip: 'Daily', unit: 'day' },
  [RecurrenceFrequency.Weekly]: { chip: 'Weekly', unit: 'week' },
  [RecurrenceFrequency.Monthly]: { chip: 'Monthly', unit: 'month' },
  [RecurrenceFrequency.Yearly]: { chip: 'Yearly', unit: 'year' },
};

const weekdayNames = (mask: number) =>
  WEEKDAYS.filter((day) => (mask & day.bit) !== 0)
    .map((day) => day.short)
    .join(', ');

// "every day" / "every 2 weeks on Mon, Wed" / "every month" / null when the item does not repeat by a rule of the app's own.
export function formatRecurrence(
  item: Pick<TaskDto, 'isRecurring' | 'recurrenceFrequency' | 'recurrenceInterval' | 'recurrenceWeekdays'>,
): string | null {
  if (!item.isRecurring || item.recurrenceFrequency === null || item.recurrenceFrequency === undefined) return null;
  const interval = item.recurrenceInterval ?? 1;
  const unit = FREQUENCY_LABEL[item.recurrenceFrequency].unit;
  const every = interval === 1 ? `every ${unit}` : `every ${interval} ${unit}s`;
  if (item.recurrenceFrequency === RecurrenceFrequency.Weekly && item.recurrenceWeekdays) {
    return `${every} on ${weekdayNames(item.recurrenceWeekdays)}`;
  }
  return every;
}

// Turning "Repeats" on: daily is the simplest rule; the weekday of the first occurrence is where a weekly rule starts.
export function defaultRecurrence(anchor: string | null): Pick<ScheduleFields, 'isRecurring' | 'recurrenceFrequency' | 'recurrenceInterval' | 'recurrenceWeekdays'> {
  return {
    isRecurring: true,
    recurrenceFrequency: RecurrenceFrequency.Daily,
    recurrenceInterval: 1,
    recurrenceWeekdays: anchor ? weekdayBit(new Date(anchor)) : null,
  };
}

// The rule of the form against the rule the item has: did the person change how it repeats? (A change applies to "this and following"
// or "all", never to "this item" alone.) A weekly rule with no stored days means "the weekday of the first occurrence".
export function recurrenceChanged(
  task: Pick<TaskDto, 'isRecurring' | 'recurrenceFrequency' | 'recurrenceInterval' | 'recurrenceWeekdays' | 'recurrenceEndsAtUtc'>,
  schedule: Pick<ScheduleFields, 'isRecurring' | 'recurrenceFrequency' | 'recurrenceInterval' | 'recurrenceWeekdays' | 'recurrenceEndsAtUtc'>,
): boolean {
  if (task.isRecurring !== schedule.isRecurring) return true;
  if (!task.isRecurring) return false;
  const endsBefore = task.recurrenceEndsAtUtc ? new Date(task.recurrenceEndsAtUtc).getTime() : null;
  const endsNow = schedule.recurrenceEndsAtUtc ? new Date(schedule.recurrenceEndsAtUtc).getTime() : null;
  return (
    task.recurrenceFrequency !== schedule.recurrenceFrequency ||
    (task.recurrenceInterval ?? 1) !== (schedule.recurrenceInterval ?? 1) ||
    (task.recurrenceFrequency === RecurrenceFrequency.Weekly && (task.recurrenceWeekdays ?? 0) !== (schedule.recurrenceWeekdays ?? 0)) ||
    endsBefore !== endsNow
  );
}

// The message of the rule in the form, or null when it is fine (the twin of the backend validator).
export function validateRecurrence(
  s: Pick<ScheduleFields, 'recurrenceFrequency' | 'recurrenceInterval' | 'recurrenceWeekdays'>,
): string | null {
  if (s.recurrenceFrequency === null) return 'Choose how often it repeats.';
  if (s.recurrenceInterval === null || !Number.isInteger(s.recurrenceInterval) || s.recurrenceInterval < 1 || s.recurrenceInterval > MAX_INTERVAL) {
    return `Repeat every 1 to ${MAX_INTERVAL}.`;
  }
  if (s.recurrenceFrequency === RecurrenceFrequency.Weekly && !s.recurrenceWeekdays) return 'Choose at least one day of the week.';
  return null;
}
