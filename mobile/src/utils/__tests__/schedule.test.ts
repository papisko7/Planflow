import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { TaskStatus, TaskType, type ScheduleFields, type TaskDto } from '../../types/task';
import { dueUtc, emptySchedule, isPastEvent, normalizeSchedule, toUpdateRequest, validateSchedule } from '../schedule';

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

describe('toUpdateRequest', () => {
  it('resends the whole item with only the override changed', () => {
    const dto: TaskDto = {
      id: '1', teamId: 't', assignedUserId: null, title: 'Standup', description: null, status: TaskStatus.Todo,
      deadlineUtc: null, impactScore: 3, blockedByTaskId: null, manualUrgencyOverride: null,
      manualUrgencyOverrideExpiresAtUtc: null, currentUrgencyScore: 0.2, createdAtUtc: 'x', updatedAtUtc: null,
      ...PLAIN_TASK_FIELDS, taskType: TaskType.Event, startUtc: '2026-10-20T08:00:00Z', endUtc: '2026-10-20T08:15:00Z',
    };
    const req = toUpdateRequest(dto, { status: TaskStatus.Done });
    expect(req.status).toBe(TaskStatus.Done);
    expect(req.taskType).toBe(TaskType.Event);
    expect(req.startUtc).toBe(dto.startUtc);
    expect(req.endUtc).toBe(dto.endUtc);
    expect(req.title).toBe('Standup');
  });
});
