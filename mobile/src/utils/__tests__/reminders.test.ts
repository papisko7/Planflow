import { ReminderAnchor, TaskType } from '../../types/task';
import { formatReminder } from '../itemDisplay';
import {
  dayReminder,
  fitReminders,
  formatTimeOfDay,
  offsetReminder,
  parseTimeOfDay,
  reminderKey,
  toReminderInput,
  validateReminders,
  type ReminderContext,
} from '../reminders';

const ctx = (over: Partial<ReminderContext> = {}): ReminderContext => ({
  taskType: TaskType.Event,
  allowsAlerts: true,
  isAllDay: false,
  hasStart: true,
  hasDeadline: false,
  ...over,
});

describe('parseTimeOfDay / formatTimeOfDay', () => {
  it.each([
    ['09:00', 540],
    ['9:30', 570],
    ['00:00', 0],
    ['23:59', 1439],
    [' 07:05 ', 425],
  ])('reads %s as %i minutes', (text, minutes) => {
    expect(parseTimeOfDay(text)).toBe(minutes);
  });

  it.each(['24:00', '12:60', '9', '9:5', 'noon', '', '12-30'])('refuses %p', (text) => {
    expect(parseTimeOfDay(text)).toBeNull();
  });

  it('writes a time of day back with leading zeros', () => {
    expect(formatTimeOfDay(540)).toBe('09:00');
    expect(formatTimeOfDay(1439)).toBe('23:59');
  });
});

describe('toReminderInput', () => {
  it('keeps the form and gives an alert from an older server the start as its anchor', () => {
    expect(toReminderInput({ offsetMinutes: 30, daysBefore: null, timeOfDayMinutes: null })).toEqual({
      offsetMinutes: 30, daysBefore: null, timeOfDayMinutes: null, anchor: ReminderAnchor.Start,
    });
    expect(toReminderInput({ offsetMinutes: null, daysBefore: 1, timeOfDayMinutes: 540, anchor: ReminderAnchor.Deadline }).anchor).toBe(ReminderAnchor.Deadline);
  });
});

describe('reminderKey', () => {
  it('tells apart the anchor and the form, and equal alerts apart from none', () => {
    expect(reminderKey(offsetReminder(30, ReminderAnchor.Start))).toBe(reminderKey(offsetReminder(30, ReminderAnchor.Start)));
    expect(reminderKey(offsetReminder(30, ReminderAnchor.Start))).not.toBe(reminderKey(offsetReminder(30, ReminderAnchor.Deadline)));
    expect(reminderKey(offsetReminder(30, ReminderAnchor.Start))).not.toBe(reminderKey(dayReminder(0, 30, ReminderAnchor.Start)));
  });
});

describe('fitReminders', () => {
  const start = ReminderAnchor.Start;
  const deadline = ReminderAnchor.Deadline;

  it('keeps everything on a plain timed event', () => {
    const list = [offsetReminder(30, start), dayReminder(1, 540, start)];

    expect(fitReminders(list, ctx())).toEqual(list);
  });

  it('drops them all for a kind without alerts (Out of office, Working location)', () => {
    expect(fitReminders([offsetReminder(30, start)], ctx({ allowsAlerts: false }))).toEqual([]);
  });

  it('drops a deadline alert once the item is an event (an event has no deadline)', () => {
    const list = [offsetReminder(30, start), offsetReminder(60, deadline)];

    expect(fitReminders(list, ctx({ taskType: TaskType.Event }))).toEqual([offsetReminder(30, start)]);
    expect(fitReminders(list, ctx({ taskType: TaskType.Task, hasDeadline: true }))).toEqual(list);
  });

  it('drops an "N minutes before" alert once the item is all-day, and keeps the day form', () => {
    const list = [offsetReminder(30, start), dayReminder(1, 540, start)];

    expect(fitReminders(list, ctx({ isAllDay: true }))).toEqual([dayReminder(1, 540, start)]);
  });
});

describe('validateReminders', () => {
  it('accepts an empty list and a sensible one', () => {
    expect(validateReminders([], ctx())).toBeNull();
    expect(validateReminders([offsetReminder(30, ReminderAnchor.Start)], ctx())).toBeNull();
  });

  it('refuses more than four, and two that are the same', () => {
    const five = [5, 10, 15, 20, 25].map((m) => offsetReminder(m, ReminderAnchor.Start));

    expect(validateReminders(five, ctx())).toMatch(/at most 4/);
    expect(validateReminders([offsetReminder(5, ReminderAnchor.Start), offsetReminder(5, ReminderAnchor.Start)], ctx())).toMatch(/same/);
  });

  it('names the moment an alert needs when the item does not have it', () => {
    expect(validateReminders([offsetReminder(5, ReminderAnchor.Deadline)], ctx({ taskType: TaskType.Task, hasDeadline: false }))).toMatch(/deadline/);
    expect(validateReminders([offsetReminder(5, ReminderAnchor.Start)], ctx({ taskType: TaskType.Task, hasStart: false }))).toMatch(/start/);
    expect(validateReminders([offsetReminder(5, ReminderAnchor.Deadline)], ctx({ taskType: TaskType.Task, hasDeadline: true }))).toBeNull();
  });
});

describe('formatReminder with the anchor', () => {
  it('leaves an event\'s alerts as they were (they count from the start)', () => {
    expect(formatReminder(offsetReminder(30, ReminderAnchor.Start))).toBe('30 minutes before');
    expect(formatReminder(offsetReminder(0, ReminderAnchor.Start))).toBe('At the start');
    expect(formatReminder(dayReminder(1, 540, ReminderAnchor.Start))).toBe('1 day before at 09:00');
  });

  it('says what a task\'s alerts count from', () => {
    expect(formatReminder(offsetReminder(120, ReminderAnchor.Deadline), TaskType.Task)).toBe('2 hours before the deadline');
    expect(formatReminder(offsetReminder(0, ReminderAnchor.Deadline), TaskType.Task)).toBe('At the deadline');
    expect(formatReminder(offsetReminder(1440, ReminderAnchor.Start), TaskType.Task)).toBe('1 day before the start');
    expect(formatReminder(dayReminder(2, 540, ReminderAnchor.Deadline), TaskType.Task)).toBe('2 days before the deadline at 09:00');
    expect(formatReminder(dayReminder(0, 480, ReminderAnchor.Deadline), TaskType.Task)).toBe('On the deadline day at 08:00');
  });

  it('is safe to use as a plain map callback argument pair (the index is not a task type)', () => {
    const texts = [offsetReminder(30, ReminderAnchor.Start), offsetReminder(60, ReminderAnchor.Start)].map((r) => formatReminder(r, TaskType.Event));

    expect(texts).toEqual(['30 minutes before', '1 hour before']);
  });
});
