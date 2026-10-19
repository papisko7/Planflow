import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { TaskStatus, TaskType, type TaskDto } from '../../types/task';
import { collapseSeries, formatSeriesMore } from '../seriesGrouping';

const DAY = 86_400_000;
const NOW = new Date('2026-10-19T10:00:00Z');
const inDays = (d: number) => new Date(NOW.getTime() + d * DAY).toISOString();

const event = (id: string, daysFromNow: number, over: Partial<TaskDto> = {}): TaskDto => ({
  id,
  teamId: 'team',
  assignedUserId: null,
  title: id,
  description: null,
  status: TaskStatus.Confirmed,
  deadlineUtc: null,
  impactScore: null,
  blockedByTaskId: null,
  manualUrgencyOverride: null,
  manualUrgencyOverrideExpiresAtUtc: null,
  currentUrgencyScore: 0,
  createdAtUtc: '2026-01-01T00:00:00Z',
  updatedAtUtc: null,
  ...PLAIN_TASK_FIELDS,
  taskType: TaskType.Event,
  startUtc: inDays(daysFromNow),
  endUtc: new Date(new Date(inDays(daysFromNow)).getTime() + 3_600_000).toISOString(),
  ...over,
});

// A repeating event is one entry in the lists about "what is there", not a copy per date.
describe('collapseSeries', () => {
  it('shows one entry per series: the next open occurrence, with the number of the others', () => {
    const items = [
      event('past', -7, { seriesId: 'weekly' }),
      event('next', 3, { seriesId: 'weekly' }),
      event('later', 10, { seriesId: 'weekly' }),
      event('latest', 17, { seriesId: 'weekly' }),
    ];

    const result = collapseSeries(items, NOW);

    expect(result.map((i) => i.id)).toEqual(['next']);
    expect(result[0].seriesMore).toBe(3);
  });

  it('a hundred yearly copies are one entry', () => {
    const copies = Array.from({ length: 100 }, (_, i) => event(`b${i}`, 5 + i * 365, { seriesId: 'birthday' }));

    const result = collapseSeries(copies, NOW);

    expect(result).toHaveLength(1);
    expect(result[0].id).toBe('b0');
    expect(result[0].seriesMore).toBe(99);
  });

  it('skips a cancelled occurrence in favour of the next one that is still on', () => {
    const items = [event('cancelled', 2, { seriesId: 's', status: TaskStatus.Cancelled }), event('on', 9, { seriesId: 's' })];

    expect(collapseSeries(items, NOW).map((i) => i.id)).toEqual(['on']);
  });

  it('a series that is over is shown by its latest occurrence', () => {
    const items = [event('old1', -30, { seriesId: 's' }), event('old2', -9, { seriesId: 's' }), event('old3', -20, { seriesId: 's' })];

    const result = collapseSeries(items, NOW);

    expect(result.map((i) => i.id)).toEqual(['old2']);
    expect(result[0].seriesMore).toBe(2);
  });

  it('leaves single events, tasks and a series of one alone, and keeps the order of the list', () => {
    const task: TaskDto = { ...event('task', 1), taskType: TaskType.Task, startUtc: null, endUtc: null, seriesId: 'looks-like-a-series' };
    const items = [event('a', 1), task, event('lonely', 2, { seriesId: 'one' }), event('b', 3, { seriesId: 'two' }), event('c', 4, { seriesId: 'two' })];

    const result = collapseSeries(items, NOW);

    expect(result.map((i) => i.id)).toEqual(['a', 'task', 'lonely', 'b']);
    expect(result.find((i) => i.id === 'lonely')?.seriesMore).toBeUndefined();
    expect(result.find((i) => i.id === 'b')?.seriesMore).toBe(1);
  });

  it('does not change the items it is given', () => {
    const items = [event('x', 1, { seriesId: 's' }), event('y', 2, { seriesId: 's' })];

    collapseSeries(items, NOW);

    expect(items[0].seriesMore).toBeUndefined();
  });
});

describe('formatSeriesMore', () => {
  it('says how many more, and nothing for none', () => {
    expect(formatSeriesMore(3)).toBe('+3 more');
    expect(formatSeriesMore(0)).toBeNull();
    expect(formatSeriesMore(undefined)).toBeNull();
  });
});
