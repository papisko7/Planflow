import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { TaskStatus, TaskType, type TaskDto } from '../../types/task';
import { computeDashboardStats } from '../dashboardStats';
import { formatWhen } from '../itemDisplay';
import { dueMoment, pickerToWireDay, validateEffort, wireDayToPicker } from '../schedule';

function item(over: Partial<TaskDto> = {}): TaskDto {
  return {
    id: 'g1', teamId: 'team', assignedUserId: null, title: 'Task', description: null, status: TaskStatus.Backlog,
    deadlineUtc: null, impactScore: null, blockedByTaskId: null, manualUrgencyOverride: null, manualUrgencyOverrideExpiresAtUtc: null,
    currentUrgencyScore: 0.5, createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: null, ...PLAIN_TASK_FIELDS, ...over,
  };
}

// A Google task is due on a DAY: the API stores that day as midnight UTC and the app must show/judge it as the same calendar day
// wherever the phone is. These tests build the expected values from LOCAL dates, so they hold in every time zone.
const googleTask = (day: string, over: Partial<TaskDto> = {}) =>
  item({ isExternal: true, isAllDay: true, deadlineUtc: `${day}T00:00:00.000Z`, ...over });

describe('a whole-day task from Google Tasks', () => {
  it('shows its due day as the date it stores, not as the clock time of midnight UTC', () => {
    const expected = new Date(2030, 9, 20).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });

    expect(formatWhen(googleTask('2030-10-20'))).toBe(expected);
  });

  it('a timed task still shows the local day of its deadline', () => {
    const local = new Date(2030, 9, 20, 18, 30);

    expect(formatWhen(item({ deadlineUtc: local.toISOString() }))).toBe(local.toLocaleDateString(undefined, { month: 'short', day: 'numeric' }));
  });

  it('runs out at the END of its day (a task due today is not late yet), a whole-day event at the START of its first day', () => {
    expect(dueMoment(googleTask('2030-10-20'))).toEqual(new Date(new Date(2030, 9, 21).getTime() - 1));
    expect(dueMoment(item({ taskType: TaskType.Event, isAllDay: true, startUtc: '2030-10-20T00:00:00.000Z' }))).toEqual(new Date(2030, 9, 20));
    const timed = new Date(2030, 9, 20, 14, 0);
    expect(dueMoment(item({ deadlineUtc: timed.toISOString() }))).toEqual(timed);
    expect(dueMoment(item())).toBeNull();
  });

  it('is due today during its day, overdue the day after, and not yet due before it', () => {
    const afternoonOfThe20th = new Date(2030, 9, 20, 15, 0);

    const stats = computeDashboardStats(
      [
        googleTask('2030-10-20', { id: 'today' }),
        googleTask('2030-10-19', { id: 'yesterday' }),
        googleTask('2030-10-21', { id: 'tomorrow' }),
      ],
      afternoonOfThe20th,
    );

    expect(stats).toMatchObject({ open: 3, overdue: 1, dueToday: 1 });
  });
});

describe('whole-day values and the date pickers', () => {
  it('a stored day becomes the end of that LOCAL day for the picker, and goes back to midnight UTC unchanged', () => {
    const picker = wireDayToPicker('2030-10-20T00:00:00.000Z')!;

    const local = new Date(picker);
    expect([local.getFullYear(), local.getMonth(), local.getDate(), local.getHours(), local.getMinutes()]).toEqual([2030, 9, 20, 23, 59]);
    expect(pickerToWireDay(picker)).toBe('2030-10-20T00:00:00.000Z');
  });

  it('keeps "no day" as no day', () => {
    expect(wireDayToPicker(null)).toBeNull();
    expect(pickerToWireDay(null)).toBeNull();
  });

  it('a day picked at any time of day is stored as that local date, not a neighbouring one', () => {
    expect(pickerToWireDay(new Date(2030, 9, 20, 0, 5).toISOString())).toBe('2030-10-20T00:00:00.000Z');
    expect(pickerToWireDay(new Date(2030, 9, 20, 23, 59).toISOString())).toBe('2030-10-20T00:00:00.000Z');
  });
});

describe('validateEffort', () => {
  it('accepts no estimate and any amount up to a year', () => {
    expect(validateEffort(null)).toBeNull();
    expect(validateEffort(0.25)).toBeNull();
    expect(validateEffort(366 * 24)).toBeNull();
  });

  it('refuses zero, negative and more than a year', () => {
    expect(validateEffort(0)).toMatch(/greater than 0/);
    expect(validateEffort(-1)).toMatch(/greater than 0/);
    expect(validateEffort(366 * 24 + 1)).toMatch(/at most one year/);
  });
});
