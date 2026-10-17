import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { TaskStatus, TaskType, type TaskDto } from '../../types/task';
import { countByTab, filterByTab } from '../itemFilter';

const HOUR = 3_600_000;
const inHours = (h: number) => new Date(Date.now() + h * HOUR).toISOString();

let n = 0;
function item(over: Partial<TaskDto>): TaskDto {
  n += 1;
  return {
    id: `i${n}`,
    teamId: 't',
    assignedUserId: null,
    title: `Item ${n}`,
    description: null,
    status: TaskStatus.Todo,
    deadlineUtc: null,
    impactScore: null,
    blockedByTaskId: null,
    manualUrgencyOverride: null,
    manualUrgencyOverrideExpiresAtUtc: null,
    currentUrgencyScore: 0,
    createdAtUtc: '2026-01-01T00:00:00Z',
    updatedAtUtc: null,
    ...PLAIN_TASK_FIELDS,
    ...over,
  };
}

describe('filterByTab', () => {
  const task1 = item({ id: 'task1' });
  const later = item({ id: 'later', taskType: TaskType.Event, startUtc: inHours(48) });
  const soon = item({ id: 'soon', taskType: TaskType.Event, startUtc: inHours(2) });
  const old = item({ id: 'old', taskType: TaskType.Event, startUtc: inHours(-30), endUtc: inHours(-29) });
  const older = item({ id: 'older', taskType: TaskType.Event, startUtc: inHours(-60), endUtc: inHours(-59) });
  const all = [old, task1, later, older, soon];

  it('keeps only tasks on the Tasks tab', () => {
    expect(filterByTab(all, 'tasks').map((i) => i.id)).toEqual(['task1']);
  });

  it('orders events: upcoming soonest first, then past newest first', () => {
    expect(filterByTab(all, 'events').map((i) => i.id)).toEqual(['soon', 'later', 'old', 'older']);
  });

  it('counts both kinds', () => {
    expect(countByTab(all)).toEqual({ tasks: 1, events: 4 });
  });
});
