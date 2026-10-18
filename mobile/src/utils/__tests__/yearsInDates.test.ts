import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { TaskStatus, TaskType, type TaskDto } from '../../types/task';
import { groupTasksByDueDate } from '../calendarGrouping';
import { formatWhen } from '../itemDisplay';

const event = (id: string, startUtc: string): TaskDto => ({
  id,
  teamId: 't',
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
  isAllDay: true,
  startUtc,
  endUtc: new Date(new Date(startUtc).getTime() + 86_400_000).toISOString(),
});

// Step 7.5.11: the same yearly or weekly event shows up under many dates, so the year is always written.
describe('the year is always shown', () => {
  it('on a card, so a birthday of 2027 is not mistaken for the one of 2026', () => {
    expect(formatWhen(event('a', '2026-10-20T00:00:00Z'))).toContain('2026');
    expect(formatWhen(event('b', '2027-10-20T00:00:00Z'))).toContain('2027');
  });

  it('in a task\'s deadline and in a timed event', () => {
    expect(formatWhen({ taskType: TaskType.Task, deadlineUtc: new Date(2028, 2, 5, 12).toISOString(), startUtc: null, endUtc: null })).toContain('2028');
    expect(formatWhen({ taskType: TaskType.Event, deadlineUtc: null, startUtc: new Date(2029, 5, 5, 9).toISOString(), endUtc: new Date(2029, 5, 5, 10).toISOString() })).toContain('2029');
  });

  it('in the section headings of the Agenda, which tell the same date of different years apart', () => {
    const { sections } = groupTasksByDueDate([event('a', '2030-10-20T00:00:00Z'), event('b', '2031-10-20T00:00:00Z')]);

    expect(sections.map((s) => s.title)).toEqual([expect.stringContaining('2030'), expect.stringContaining('2031')]);
    expect(sections[0].title).not.toBe(sections[1].title);
  });

  it('even in the heading of today', () => {
    const today = new Date();
    const midnightUtc = new Date(Date.UTC(today.getFullYear(), today.getMonth(), today.getDate())).toISOString();

    const { sections } = groupTasksByDueDate([event('now', midnightUtc)]);

    expect(sections[0].title).toMatch(/^Today · /);
    expect(sections[0].title).toContain(String(today.getFullYear()));
  });
});
