import { selectOneNextAction } from '../oneNextAction';
import { TaskStatus, type TaskDto } from '../../types/task';

let nextId = 0;

function buildTask(overrides: Partial<TaskDto> = {}): TaskDto {
  nextId += 1;
  return {
    id: `task-${nextId}`,
    teamId: 'team-1',
    assignedUserId: null,
    title: `Task ${nextId}`,
    description: null,
    status: TaskStatus.Todo,
    deadlineUtc: null,
    impactScore: 1,
    blockedByTaskId: null,
    manualUrgencyOverride: null,
    manualUrgencyOverrideExpiresAtUtc: null,
    currentUrgencyScore: 0.5,
    createdAtUtc: '2026-01-01T00:00:00.000Z',
    updatedAtUtc: null,
    ...overrides,
  };
}

describe('selectOneNextAction', () => {
  it('returns null for an empty task list', () => {
    expect(selectOneNextAction([])).toBeNull();
  });

  it('returns null when every task is done, cancelled, or blocked', () => {
    const tasks = [
      buildTask({ status: TaskStatus.Done }),
      buildTask({ status: TaskStatus.Cancelled }),
      buildTask({ status: TaskStatus.Blocked }),
    ];
    expect(selectOneNextAction(tasks)).toBeNull();
  });

  it('picks the highest urgency score among active tasks', () => {
    const low = buildTask({ currentUrgencyScore: 0.3 });
    const high = buildTask({ currentUrgencyScore: 0.9 });
    const done = buildTask({ currentUrgencyScore: 0.99, status: TaskStatus.Done });

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

  it('excludes blocked, done, and cancelled tasks even when they would otherwise outrank the winner', () => {
    const active = buildTask({ currentUrgencyScore: 0.5 });
    const blocked = buildTask({ currentUrgencyScore: 0.95, status: TaskStatus.Blocked });
    const done = buildTask({ currentUrgencyScore: 0.95, status: TaskStatus.Done });
    const cancelled = buildTask({ currentUrgencyScore: 0.95, status: TaskStatus.Cancelled });

    expect(selectOneNextAction([blocked, done, cancelled, active])).toBe(active);
  });

  it('includes in-progress tasks as eligible candidates', () => {
    const inProgress = buildTask({ status: TaskStatus.InProgress, currentUrgencyScore: 0.6 });
    expect(selectOneNextAction([inProgress])).toBe(inProgress);
  });
});
