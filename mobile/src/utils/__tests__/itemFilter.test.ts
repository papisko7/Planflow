import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { TaskStatus, TaskType, type TaskDto } from '../../types/task';
import { buildRows, countByTab, filterByTab } from '../itemFilter';

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
    status: TaskStatus.Backlog,
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

  it('counts both kinds and the total', () => {
    expect(countByTab(all)).toEqual({ all: 5, tasks: 1, events: 4 });
  });

  it('All lists the tasks first, then the events in their own order', () => {
    expect(filterByTab(all, 'all').map((i) => i.id)).toEqual(['task1', 'soon', 'later', 'old', 'older']);
  });
});

describe('buildRows', () => {
  const task1 = item({ id: 'task1' });
  const task2 = item({ id: 'task2' });
  const soon = item({ id: 'soon', taskType: TaskType.Event, startUtc: inHours(2) });

  it('All puts a heading with a count above the Tasks and above the Events', () => {
    const rows = buildRows([soon, task1, task2], 'all');

    expect(rows.map((r) => (r.type === 'header' ? `${r.title} ${r.count}` : r.item.id))).toEqual([
      'Tasks 2', 'task1', 'task2', 'Events 1', 'soon',
    ]);
  });

  it('All leaves out the heading of an empty section', () => {
    expect(buildRows([task1], 'all').map((r) => r.type)).toEqual(['header', 'item']);
    expect(buildRows([], 'all')).toEqual([]);
  });

  it('the Tasks and Events tabs are plain lists without headings', () => {
    expect(buildRows([soon, task1], 'tasks').map((r) => r.type)).toEqual(['item']);
    expect(buildRows([soon, task1], 'events').map((r) => r.type)).toEqual(['item']);
  });
});
