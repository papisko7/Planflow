import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { TaskStatus, TaskType, type ScheduleFields, type TaskDto } from '../../types/task';
import {
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
  validateSchedule,
} from '../schedule';

const HOUR = 3_600_000;
const inHours = (h: number) => new Date(Date.now() + h * HOUR).toISOString();

const event = (over: Partial<ScheduleFields> = {}): ScheduleFields => ({ ...emptySchedule(TaskType.Event), ...over });
const task = (over: Partial<ScheduleFields> = {}): ScheduleFields => ({ ...emptySchedule(TaskType.Task), ...over });

describe('normalizeSchedule', () => {
  it('drops event dates from a Task and the deadline from an Event', () => {
    const asTask = normalizeSchedule(task({ deadlineUtc: inHours(5), startUtc: inHours(1), endUtc: inHours(2) }));
    expect(asTask.startUtc).toBeNull();
    expect(asTask.endUtc).toBeNull();
    expect(asTask.deadlineUtc).not.toBeNull();

    const asEvent = normalizeSchedule(event({ deadlineUtc: inHours(5), startUtc: inHours(1) }));
    expect(asEvent.deadlineUtc).toBeNull();
    expect(asEvent.startUtc).not.toBeNull();
  });

  it('drops recurrence settings when not recurring', () => {
    const n = normalizeSchedule(task({ isRecurring: false, recurrenceIntervalHours: 24, recurrenceEndsAtUtc: inHours(100) }));
    expect(n.recurrenceIntervalHours).toBeNull();
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

  it('needs an interval and a first occurrence for recurrence', () => {
    expect(validateSchedule(task({ isRecurring: true, deadlineUtc: inHours(5) }), true)).toMatch(/repeats/i);
    expect(validateSchedule(task({ isRecurring: true, recurrenceIntervalHours: 24 }), true)).toMatch(/deadline/i);
    expect(validateSchedule(task({ isRecurring: true, recurrenceIntervalHours: 24, deadlineUtc: inHours(5) }), true)).toBeNull();
  });

  it('rejects a recurrence ending before the first occurrence', () => {
    const s = task({ isRecurring: true, recurrenceIntervalHours: 24, deadlineUtc: inHours(50), recurrenceEndsAtUtc: inHours(10) });
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
