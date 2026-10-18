import { selectOneNextAction } from '../oneNextAction';
import { TaskStatus, type TaskDto } from '../../types/task';
import { PLAIN_TASK_FIELDS } from '../../test-fixtures';

let nextId = 0;

function buildTask(overrides: Partial<TaskDto> = {}): TaskDto {
  nextId += 1;
  return {
    id: `task-${nextId}`,
    teamId: 'team-1',
    assignedUserId: null,
    title: `Task ${nextId}`,
    description: null,
    status: TaskStatus.Backlog,
    deadlineUtc: null,
    impactScore: 1,
    blockedByTaskId: null,
    manualUrgencyOverride: null,
    manualUrgencyOverrideExpiresAtUtc: null,
    currentUrgencyScore: 0.5,
    createdAtUtc: '2026-01-01T00:00:00.000Z',
    updatedAtUtc: null,
    ...PLAIN_TASK_FIELDS,
    ...overrides,
  };
}

describe('selectOneNextAction', () => {
  it('returns null for an empty task list', () => {
    expect(selectOneNextAction([])).toBeNull();
  });

  it('returns null when every task is completed or cancelled', () => {
    const tasks = [buildTask({ status: TaskStatus.Completed }), buildTask({ status: TaskStatus.Cancelled })];
    expect(selectOneNextAction(tasks)).toBeNull();
  });

  it('offers the blocker instead of the more urgent task that waits on it', () => {
    // The blocked task cannot be started yet, so the only actionable item is the blocker.
    const blocker = buildTask({ currentUrgencyScore: 0.2 });
    const blocked = buildTask({ currentUrgencyScore: 0.9, blockedByTaskId: blocker.id });
    expect(selectOneNextAction([blocked, blocker])).toBe(blocker);
  });

  it('picks the highest urgency score among active tasks', () => {
    const low = buildTask({ currentUrgencyScore: 0.3 });
    const high = buildTask({ currentUrgencyScore: 0.9 });
    const done = buildTask({ currentUrgencyScore: 0.99, status: TaskStatus.Completed });

    expect(selectOneNextAction([low, high, done])).toBe(high);
  });

  it('breaks a tied urgency score by the earliest deadline', () => {
    const later = buildTask({ currentUrgencyScore: 0.7, deadlineUtc: '2026-02-10T00:00:00.000Z' });
    const sooner = buildTask({ currentUrgencyScore: 0.7, deadlineUtc: '2026-02-01T00:00:00.000Z' });

    expect(selectOneNextAction([later, sooner])).toBe(sooner);
  });

  it('treats a missing deadline as lowest priority within a tied urgency score', () => {
    const noDeadline = buildTask({ currentUrgencyScore: 0.7, deadlineUtc: null });
    const withDeadline = buildTask({ currentUrgencyScore: 0.7, deadlineUtc: '2026-03-01T00:00:00.000Z' });

    expect(selectOneNextAction([noDeadline, withDeadline])).toBe(withDeadline);
  });

  it('breaks a tied urgency score and deadline by the highest impact', () => {
    const lowImpact = buildTask({ currentUrgencyScore: 0.7, deadlineUtc: null, impactScore: 2 });
    const highImpact = buildTask({ currentUrgencyScore: 0.7, deadlineUtc: null, impactScore: 8 });

    expect(selectOneNextAction([lowImpact, highImpact])).toBe(highImpact);
  });

  it('falls back to the oldest creation date when everything else ties', () => {
    const newer = buildTask({
      currentUrgencyScore: 0.7,
      deadlineUtc: null,
      impactScore: 5,
      createdAtUtc: '2026-01-05T00:00:00.000Z',
    });
    const older = buildTask({
      currentUrgencyScore: 0.7,
      deadlineUtc: null,
      impactScore: 5,
      createdAtUtc: '2026-01-01T00:00:00.000Z',
    });

    expect(selectOneNextAction([newer, older])).toBe(older);
  });

  it('excludes completed and cancelled tasks even when they would otherwise outrank the winner', () => {
    const active = buildTask({ currentUrgencyScore: 0.5 });
    const done = buildTask({ currentUrgencyScore: 0.95, status: TaskStatus.Completed });
    const cancelled = buildTask({ currentUrgencyScore: 0.95, status: TaskStatus.Cancelled });

    expect(selectOneNextAction([done, cancelled, active])).toBe(active);
  });

  it('skips a task that is blocked by an unfinished task, however urgent it is', () => {
    const blocker = buildTask({ currentUrgencyScore: 0.4 });
    const blocked = buildTask({ currentUrgencyScore: 0.95, blockedByTaskId: blocker.id });
    const other = buildTask({ currentUrgencyScore: 0.5 });

    expect(selectOneNextAction([blocked, other, blocker])).toBe(other);
  });

  it('treats a task as unblocked once its blocker is completed', () => {
    const blocker = buildTask({ status: TaskStatus.Completed });
    const unblocked = buildTask({ currentUrgencyScore: 0.9, blockedByTaskId: blocker.id });

    expect(selectOneNextAction([blocker, unblocked])).toBe(unblocked);
  });

  it('treats a task as unblocked when its blocker is not in the list at all', () => {
    const task = buildTask({ blockedByTaskId: 'somewhere-else' });
    expect(selectOneNextAction([task])).toBe(task);
  });

  it('includes in-progress tasks as eligible candidates', () => {
    const inProgress = buildTask({ status: TaskStatus.InProgress, currentUrgencyScore: 0.6 });
    expect(selectOneNextAction([inProgress])).toBe(inProgress);
  });
});
