import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { EventTemplate, TaskStatus, TaskType, type TaskDto } from '../../types/task';
import { computeDashboardStats } from '../dashboardStats';
import { selectOneNextAction } from '../oneNextAction';
import { isInformational } from '../templateRules';
import { groupTasksByUrgency } from '../urgencyGrouping';

let nextId = 0;

function item(overrides: Partial<TaskDto> = {}): TaskDto {
  nextId += 1;
  return {
    id: `item-${nextId}`,
    teamId: 'team-1',
    assignedUserId: null,
    title: `Item ${nextId}`,
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

const eventWith = (eventTemplate: EventTemplate, over: Partial<TaskDto> = {}) =>
  item({
    taskType: TaskType.Event, eventTemplate, status: TaskStatus.Confirmed, currentUrgencyScore: 0,
    startUtc: new Date(Date.now() + 3_600_000).toISOString(), endUtc: new Date(Date.now() + 7_200_000).toISOString(), ...over,
  });

// Out of office, Focus time, Working location and Birthday have no score and are outside the ranking.
describe('informational items', () => {
  it('are recognised by their template; tasks and plain events are ranked', () => {
    expect(isInformational({ taskType: TaskType.Task, eventTemplate: EventTemplate.Default })).toBe(false);
    expect(isInformational({ taskType: TaskType.Event, eventTemplate: EventTemplate.Default })).toBe(false);
    for (const template of [EventTemplate.OutOfOffice, EventTemplate.FocusTime, EventTemplate.WorkingLocation, EventTemplate.Birthday]) {
      expect(isInformational({ taskType: TaskType.Event, eventTemplate: template })).toBe(true);
    }
  });

  it('are left out of the urgency groups of the dashboard', () => {
    const sections = groupTasksByUrgency([item({ currentUrgencyScore: 0.9 }), eventWith(EventTemplate.OutOfOffice), eventWith(EventTemplate.Birthday)]);

    expect(sections.map((s) => s.tier)).toEqual(['critical']);
    expect(sections.flatMap((s) => s.data)).toHaveLength(1);
  });

  it('are never offered as the next action, even when nothing else is open', () => {
    const task = item({ currentUrgencyScore: 0.1 });

    expect(selectOneNextAction([eventWith(EventTemplate.FocusTime, { currentUrgencyScore: 0.99 }), task])).toBe(task);
    expect(selectOneNextAction([eventWith(EventTemplate.WorkingLocation)])).toBeNull();
  });

  it('do not count as open work, overdue or due today in the statistics', () => {
    const stats = computeDashboardStats([item(), eventWith(EventTemplate.OutOfOffice), eventWith(EventTemplate.Birthday)]);

    expect(stats.open).toBe(1);
    expect(stats.total).toBe(1);
  });
});
