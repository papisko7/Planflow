import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { ItemListKind } from '../../types/itemList';
import { TaskStatus, TaskType, type TaskDto } from '../../types/task';
import { commonStatuses, expandToSeries, groupByTeam, listKindFor } from '../selection';

const item = (id: string, over: Partial<TaskDto> = {}): TaskDto => ({
  id,
  teamId: 'team',
  assignedUserId: null,
  title: id,
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
});
const event = (id: string, over: Partial<TaskDto> = {}) => item(id, { taskType: TaskType.Event, status: TaskStatus.Confirmed, ...over });

describe('commonStatuses', () => {
  it('tasks can take the four task statuses', () => {
    expect(commonStatuses([item('a'), item('b')])).toEqual([TaskStatus.Backlog, TaskStatus.InProgress, TaskStatus.Completed, TaskStatus.Cancelled]);
  });

  it('events can take the three event statuses', () => {
    expect(commonStatuses([event('a')])).toEqual([TaskStatus.Confirmed, TaskStatus.Tentative, TaskStatus.Cancelled]);
  });

  it('a mix of tasks and events can only be cancelled together', () => {
    expect(commonStatuses([item('a'), event('b')])).toEqual([TaskStatus.Cancelled]);
  });

  it('nothing ticked offers nothing', () => {
    expect(commonStatuses([])).toEqual([]);
  });
});

describe('expandToSeries', () => {
  const entry = event('next', { seriesId: 'weekly', seriesMore: 2 });
  const all = [entry, event('second', { seriesId: 'weekly' }), event('third', { seriesId: 'weekly' }), event('other', { seriesId: 'daily' }), item('task')];

  it('an entry that stands for a series acts for every occurrence of it', () => {
    expect(expandToSeries([entry], all).map((i) => i.id)).toEqual(['next', 'second', 'third']);
  });

  it('a single occurrence (the Agenda) acts for itself only', () => {
    const single = event('second', { seriesId: 'weekly' });

    expect(expandToSeries([single], all)).toEqual([single]);
  });

  it('other ticked items stay in the selection next to the expanded series', () => {
    const task = all[4];

    expect(expandToSeries([task, entry], all).map((i) => i.id).sort()).toEqual(['next', 'second', 'task', 'third']);
  });
});

describe('listKindFor', () => {
  it('tasks go to a task list, events to a meeting list, a mix to none', () => {
    expect(listKindFor([item('a')])).toBe(ItemListKind.TaskList);
    expect(listKindFor([event('a'), event('b')])).toBe(ItemListKind.MeetingList);
    expect(listKindFor([item('a'), event('b')])).toBeNull();
    expect(listKindFor([])).toBeNull();
  });
});

describe('groupByTeam', () => {
  it('splits the items by team, keeping the order', () => {
    const groups = groupByTeam([item('a', { teamId: 'x' }), item('b', { teamId: 'y' }), item('c', { teamId: 'x' })]);

    expect([...groups.keys()]).toEqual(['x', 'y']);
    expect(groups.get('x')!.map((i) => i.id)).toEqual(['a', 'c']);
  });
});
