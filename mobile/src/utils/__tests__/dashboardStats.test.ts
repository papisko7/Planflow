import { computeDashboardStats, formatAgo } from '../dashboardStats';
import { TaskStatus, type TaskDto } from '../../types/task';

const NOW = new Date('2026-10-05T12:00:00');

function task(overrides: Partial<TaskDto>): TaskDto {
  return {
    id: 't',
    teamId: 'team',
    assignedUserId: null,
    title: 'Task',
    description: null,
    status: TaskStatus.Todo,
    deadlineUtc: null,
    impactScore: 1,
    blockedByTaskId: null,
    manualUrgencyOverride: null,
    manualUrgencyOverrideExpiresAtUtc: null,
    currentUrgencyScore: 0.5,
    createdAtUtc: '2026-01-01T00:00:00Z',
    updatedAtUtc: null,
    ...overrides,
  };
}

describe('computeDashboardStats', () => {
  it('counts open, overdue, due today and done tasks', () => {
    const stats = computeDashboardStats(
      [
        task({ id: '1' }),
        task({ id: '2', deadlineUtc: new Date('2026-10-04T10:00:00').toISOString() }),
        task({ id: '3', deadlineUtc: new Date('2026-10-05T18:00:00').toISOString() }),
        task({ id: '4', status: TaskStatus.Done }),
        task({ id: '5', status: TaskStatus.Cancelled }),
      ],
      NOW,
    );

    expect(stats).toEqual({ open: 3, overdue: 1, dueToday: 1, done: 1, total: 4 });
  });

  it('does not count a finished task as overdue', () => {
    const stats = computeDashboardStats(
      [task({ status: TaskStatus.Done, deadlineUtc: new Date('2026-10-01T10:00:00').toISOString() })],
      NOW,
    );

    expect(stats.overdue).toBe(0);
    expect(stats.done).toBe(1);
  });
});

describe('formatAgo', () => {
  it('formats minutes, hours and days', () => {
    expect(formatAgo(new Date('2026-10-05T11:59:40'), NOW)).toBe('just now');
    expect(formatAgo(new Date('2026-10-05T11:55:00'), NOW)).toBe('5 min ago');
    expect(formatAgo(new Date('2026-10-05T09:00:00'), NOW)).toBe('3 h ago');
    expect(formatAgo(new Date('2026-10-03T12:00:00'), NOW)).toBe('2 d ago');
  });
});
