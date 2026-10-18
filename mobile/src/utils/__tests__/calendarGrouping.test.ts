import { groupTasksByDueDate } from '../calendarGrouping';
import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { TaskStatus, TaskType, type TaskDto } from '../../types/task';

const item = (id: string, over: Partial<TaskDto>): TaskDto => ({
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
  ...over,
});

describe('groupTasksByDueDate', () => {
  it('puts an imported all-day event on its own date, not on the day its midnight UTC falls on for the viewer', () => {
    const allDay = item('holiday', { taskType: TaskType.Event, isAllDay: true, startUtc: '2026-10-20T00:00:00Z', endUtc: '2026-10-21T00:00:00Z' });

    const { sections } = groupTasksByDueDate([allDay]);

    expect(sections.map((s) => s.dateKey)).toEqual(['2026-10-20']);
  });

  it("keeps a timed event on the viewer's local day", () => {
    const timed = item('late', {
      taskType: TaskType.Event,
      startUtc: new Date(2026, 9, 20, 23, 30).toISOString(),
      endUtc: new Date(2026, 9, 21, 0, 30).toISOString(),
    });

    expect(groupTasksByDueDate([timed]).sections.map((s) => s.dateKey)).toEqual(['2026-10-20']);
  });

  it('lists items without a due moment separately', () => {
    const { sections, undated } = groupTasksByDueDate([item('someday', {})]);

    expect(sections).toEqual([]);
    expect(undated.map((t) => t.id)).toEqual(['someday']);
  });
});
