import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { RecurrenceFrequency, TaskStatus, TaskType, type ScheduleFields, type TaskDto } from '../../types/task';
import {
  allDayDays,
  allDayToTimed,
  dueUtc,
  emptySchedule,
  isAllDayRange,
  isPastEvent,
  normalizeSchedule,
  pairAllDayDates,
  pairEventTimes,
  scheduleForEditing,
  toUpdateRequest,
  utcDay,
  validateSchedule,
} from '../schedule';

const HOUR = 3_600_000;
const inHours = (h: number) => new Date(Date.now() + h * HOUR).toISOString();

const event = (over: Partial<ScheduleFields> = {}): ScheduleFields => ({ ...emptySchedule(TaskType.Event), ...over });
const task = (over: Partial<ScheduleFields> = {}): ScheduleFields => ({ ...emptySchedule(TaskType.Task), ...over });
const DAILY = { recurrenceFrequency: RecurrenceFrequency.Daily, recurrenceInterval: 1 };

describe('normalizeSchedule', () => {
  it('drops the end from a Task (it keeps its planned start) and the deadline from an Event', () => {
    const asTask = normalizeSchedule(task({ deadlineUtc: inHours(5), startUtc: inHours(1), endUtc: inHours(2) }));
    expect(asTask.startUtc).not.toBeNull(); // a Task's optional planned start (step 7.5.4)
    expect(asTask.endUtc).toBeNull();
    expect(asTask.deadlineUtc).not.toBeNull();

    const asEvent = normalizeSchedule(event({ deadlineUtc: inHours(5), startUtc: inHours(1) }));
    expect(asEvent.deadlineUtc).toBeNull();
    expect(asEvent.startUtc).not.toBeNull();
  });

  it('drops recurrence settings when not recurring', () => {
    const n = normalizeSchedule(task({ isRecurring: false, recurrenceFrequency: RecurrenceFrequency.Daily, recurrenceInterval: 1, recurrenceWeekdays: 1, recurrenceEndsAtUtc: inHours(100) }));
    expect(n.recurrenceFrequency).toBeNull();
    expect(n.recurrenceInterval).toBeNull();
    expect(n.recurrenceWeekdays).toBeNull();
    expect(n.recurrenceEndsAtUtc).toBeNull();
  });
});

describe('validateSchedule', () => {
  it('accepts an empty Task', () => {
    expect(validateSchedule(task(), true)).toBeNull();
  });

  it('requires a start for an Event', () => {
    expect(validateSchedule(event(), true)).toMatch(/start/i);
  });

  it('rejects an Event ending before it starts', () => {
    expect(validateSchedule(event({ startUtc: inHours(5), endUtc: inHours(3) }), true)).toMatch(/earlier/i);
  });

  it('rejects a new event that is already over, but allows editing a past one', () => {
    const past = event({ startUtc: inHours(-5), endUtc: inHours(-4) });
    expect(validateSchedule(past, true)).toMatch(/already over/i);
    expect(validateSchedule(past, false)).toBeNull();
  });

  it('rejects a past deadline only for new tasks', () => {
    expect(validateSchedule(task({ deadlineUtc: inHours(-1) }), true)).toMatch(/future/i);
    expect(validateSchedule(task({ deadlineUtc: inHours(-1) }), false)).toBeNull();
  });

  it('rejects non-positive effort', () => {
    expect(validateSchedule(task({ estimatedEffortHours: 0 }), true)).toMatch(/effort/i);
  });

  it('needs a frequency, an interval and a first occurrence for recurrence', () => {
    expect(validateSchedule(task({ isRecurring: true, deadlineUtc: inHours(5) }), true)).toMatch(/how often/i);
    expect(validateSchedule(task({ isRecurring: true, ...DAILY }), true)).toMatch(/deadline/i);
    expect(validateSchedule(task({ isRecurring: true, ...DAILY, deadlineUtc: inHours(5) }), true)).toBeNull();
  });

  it('keeps the interval between 1 and 99 and a weekly rule on at least one day', () => {
    const base = { isRecurring: true, deadlineUtc: inHours(5) };
    expect(validateSchedule(task({ ...base, recurrenceFrequency: RecurrenceFrequency.Daily, recurrenceInterval: 0 }), true)).toMatch(/1 to 99/);
    expect(validateSchedule(task({ ...base, recurrenceFrequency: RecurrenceFrequency.Daily, recurrenceInterval: 100 }), true)).toMatch(/1 to 99/);
    expect(validateSchedule(task({ ...base, recurrenceFrequency: RecurrenceFrequency.Weekly, recurrenceInterval: 1 }), true)).toMatch(/day of the week/);
    expect(validateSchedule(task({ ...base, recurrenceFrequency: RecurrenceFrequency.Weekly, recurrenceInterval: 1, recurrenceWeekdays: 5 }), true)).toBeNull();
  });

  it('rejects a recurrence ending before the first occurrence', () => {
    const s = task({ isRecurring: true, ...DAILY, deadlineUtc: inHours(50), recurrenceEndsAtUtc: inHours(10) });
    expect(validateSchedule(s, true)).toMatch(/end after/i);
  });
});

describe('dueUtc / isPastEvent', () => {
  it('uses the deadline for a Task and the start for an Event', () => {
    expect(dueUtc({ taskType: TaskType.Task, deadlineUtc: 'D', startUtc: null })).toBe('D');
    expect(dueUtc({ taskType: TaskType.Event, deadlineUtc: null, startUtc: 'S' })).toBe('S');
  });

  it('treats an event with no end as lasting one hour, and never treats a Task as past', () => {
    expect(isPastEvent({ taskType: TaskType.Event, startUtc: inHours(-2), endUtc: null })).toBe(true);
    expect(isPastEvent({ taskType: TaskType.Event, startUtc: inHours(-0.5), endUtc: null })).toBe(false);
    expect(isPastEvent({ taskType: TaskType.Task, startUtc: null, endUtc: null })).toBe(false);
  });
});

describe('pairEventTimes', () => {
  const start = '2026-10-19T09:00:00.000Z';
  const end = '2026-10-19T10:00:00.000Z';

  it('fills the end with start + 1h when only the start is known', () => {
    expect(pairEventTimes(start, null, 'start')).toEqual({ startUtc: start, endUtc: end });
  });

  it('fills the start with end - 1h when the end is picked first', () => {
    expect(pairEventTimes(null, end, 'end')).toEqual({ startUtc: start, endUtc: end });
  });

  it('keeps a range that is already consistent', () => {
    const later = '2026-10-19T15:30:00.000Z';
    expect(pairEventTimes(start, later, 'start')).toEqual({ startUtc: start, endUtc: later });
    expect(pairEventTimes(start, later, 'end')).toEqual({ startUtc: start, endUtc: later });
  });

  it('repairs the other side when the new value would invert the range', () => {
    // start moved past the old end -> end follows
    expect(pairEventTimes('2026-10-19T12:00:00.000Z', end, 'start').endUtc).toBe('2026-10-19T13:00:00.000Z');
    // end moved before the old start -> start follows
    expect(pairEventTimes(start, '2026-10-19T07:00:00.000Z', 'end').startUtc).toBe('2026-10-19T06:00:00.000Z');
  });
});

describe('scheduleForEditing', () => {
  const dto = {
    ...PLAIN_TASK_FIELDS, taskType: TaskType.Event, startUtc: '2026-10-19T09:00:00.000Z', endUtc: null,
  } as TaskDto;

  it('opens an imported event without an end with the end the backend assumes (start + 1h)', () => {
    expect(scheduleForEditing(dto).endUtc).toBe('2026-10-19T10:00:00.000Z');
  });

  it('leaves tasks and events with an end untouched', () => {
    const withEnd = { ...dto, endUtc: '2026-10-19T11:00:00.000Z' } as TaskDto;
    expect(scheduleForEditing(withEnd).endUtc).toBe('2026-10-19T11:00:00.000Z');
    expect(scheduleForEditing({ ...dto, taskType: TaskType.Task } as TaskDto).endUtc).toBeNull();
  });
});

describe('toUpdateRequest', () => {
  it('resends the whole item with only the override changed', () => {
    const dto: TaskDto = {
      id: '1', teamId: 't', assignedUserId: null, title: 'Standup', description: null, status: TaskStatus.Backlog,
      deadlineUtc: null, impactScore: 3, blockedByTaskId: null, manualUrgencyOverride: null,
      manualUrgencyOverrideExpiresAtUtc: null, currentUrgencyScore: 0.2, createdAtUtc: 'x', updatedAtUtc: null,
      ...PLAIN_TASK_FIELDS, taskType: TaskType.Event, startUtc: '2026-10-20T08:00:00Z', endUtc: '2026-10-20T08:15:00Z',
    };
    const req = toUpdateRequest(dto, { status: TaskStatus.Completed });
    expect(req.status).toBe(TaskStatus.Completed);
    expect(req.taskType).toBe(TaskType.Event);
    expect(req.startUtc).toBe(dto.startUtc);
    expect(req.endUtc).toBe(dto.endUtc);
    expect(req.title).toBe('Standup');
  });
});

describe('all-day events', () => {
  const at = (day: number, h: number, m = 0) => new Date(2026, 9, day, h, m).toISOString();
  const local = (iso: string | null) => {
    const d = new Date(iso!);
    return [d.getDate(), d.getHours(), d.getMinutes()];
  };

  it('recognises the whole-day shape (00:00 -> 23:59) and nothing else', () => {
    expect(isAllDayRange(at(19, 0), at(19, 23, 59))).toBe(true);
    expect(isAllDayRange(at(19, 0), at(21, 23, 59))).toBe(true); // several days
    expect(isAllDayRange(at(19, 9), at(19, 10))).toBe(false);
    expect(isAllDayRange(at(19, 0), null)).toBe(false);
  });

  it('start is the base: picking a start day makes the event last that day', () => {
    const range = pairAllDayDates(at(19, 14, 30), null, 'start');
    expect(local(range.startUtc)).toEqual([19, 0, 0]);
    expect(local(range.endUtc)).toEqual([19, 23, 59]);
  });

  it('keeps a later end day, but pulls an earlier one back to the start day', () => {
    expect(local(pairAllDayDates(at(19, 0), at(22, 23, 59), 'start').endUtc)).toEqual([22, 23, 59]);
    expect(local(pairAllDayDates(at(25, 0), at(22, 23, 59), 'start').endUtc)).toEqual([25, 23, 59]);
  });

  it('picking only an end day fills the start with the same day', () => {
    const range = pairAllDayDates(null, at(19, 23, 59), 'end');
    expect(local(range.startUtc)).toEqual([19, 0, 0]);
    expect(local(range.endUtc)).toEqual([19, 23, 59]);
  });

  it('turning all-day off gives a 09:00-10:00 slot on the first day', () => {
    const range = allDayToTimed(at(19, 0));
    expect(local(range.startUtc)).toEqual([19, 9, 0]);
    expect(local(range.endUtc)).toEqual([19, 10, 0]);
    expect(allDayToTimed(null)).toEqual({ startUtc: null, endUtc: null });
  });
});

describe('imported all-day events (dates stored as midnight UTC)', () => {
  // A two-day conference on 20 and 21 October. Google's end is the midnight AFTER the last day.
  const imported = { startUtc: '2026-10-20T00:00:00Z', endUtc: '2026-10-22T00:00:00Z', isAllDay: true };

  it("utcDay reads the date from the UTC parts, so the day never shifts with the viewer's zone", () => {
    const day = utcDay('2026-10-20T00:00:00Z');
    expect([day.getFullYear(), day.getMonth(), day.getDate()]).toEqual([2026, 9, 20]);
  });

  it('allDayDays gives the first and the LAST day (not the stored midnight after it)', () => {
    const days = allDayDays(imported)!;
    expect(days.first.getDate()).toBe(20);
    expect(days.last.getDate()).toBe(21);
  });

  it('a one-day event is a single day', () => {
    const days = allDayDays({ startUtc: '2026-10-20T00:00:00Z', endUtc: '2026-10-21T00:00:00Z', isAllDay: true })!;
    expect(days.first.getTime()).toBe(days.last.getTime());
  });

  it('an imported all-day event without an end covers just its first day', () => {
    const days = allDayDays({ startUtc: '2026-10-20T00:00:00Z', endUtc: null, isAllDay: true })!;
    expect(days.first.getTime()).toBe(days.last.getTime());
  });

  it('still recognises the older all-day shape (local 00:00 to local 23:59) without the flag', () => {
    const start = new Date(2026, 9, 20, 0, 0).toISOString();
    const end = new Date(2026, 9, 21, 23, 59).toISOString();
    const days = allDayDays({ startUtc: start, endUtc: end, isAllDay: false })!;
    expect([days.first.getDate(), days.last.getDate()]).toEqual([20, 21]);
  });

  it('a timed event is not all-day', () => {
    expect(allDayDays({ startUtc: '2026-10-20T08:00:00Z', endUtc: '2026-10-20T09:00:00Z', isAllDay: false })).toBeNull();
    expect(allDayDays({ startUtc: null, endUtc: null, isAllDay: true })).toBeNull();
  });

  it('is over only after its last day is over for the viewer, not at midnight UTC', () => {
    const item = { taskType: TaskType.Event, ...imported };
    expect(isPastEvent(item, new Date(2026, 9, 21, 23, 0))).toBe(false); // evening of the last day
    expect(isPastEvent(item, new Date(2026, 9, 22, 0, 30))).toBe(true); // just after local midnight
    expect(isPastEvent(item, new Date(2026, 9, 20, 12, 0))).toBe(false);
  });
});

// Step 7.5.4: a Task may carry a planned start, which can not be later than its deadline.
describe('validateSchedule - a task\'s planned start', () => {
  it('accepts a start before the deadline, and a task without a start', () => {
    expect(validateSchedule(task({ startUtc: inHours(2), deadlineUtc: inHours(10) }), true)).toBeNull();
    expect(validateSchedule(task({ deadlineUtc: inHours(10) }), true)).toBeNull();
  });

  it('rejects a start after the deadline, also when editing', () => {
    expect(validateSchedule(task({ startUtc: inHours(20), deadlineUtc: inHours(10) }), true)).toMatch(/start must not be after the deadline/i);
    expect(validateSchedule(task({ startUtc: inHours(-1), deadlineUtc: inHours(-5) }), false)).toMatch(/start must not be after the deadline/i);
  });

  it('a start in the past is fine (the work may already be planned to be under way)', () => {
    expect(validateSchedule(task({ startUtc: inHours(-3), deadlineUtc: inHours(10) }), true)).toBeNull();
  });
});
