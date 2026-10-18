import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { ItemListKind, type ItemListDto } from '../../types/itemList';
import { TaskStatus, TaskType, type TaskDto } from '../../types/task';
import { buildRows } from '../itemFilter';

const HOUR = 3_600_000;
const inHours = (h: number) => new Date(Date.now() + h * HOUR).toISOString();

const item = (id: string, over: Partial<TaskDto> = {}): TaskDto => ({
  id,
  teamId: 't',
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

const list = (id: string, name: string, kind: ItemListKind = ItemListKind.TaskList): ItemListDto => ({ id, teamId: 't', name, kind, createdByUserId: 'u' });

// Step 7.5.11: the team's own lists show as groups next to the Google Tasks lists.
describe('buildRows with the team\'s own lists', () => {
  const sprint = list('l-sprint', 'Sprint');
  const home = list('l-home', 'Home');
  const standups = list('l-standups', 'Standups', ItemListKind.MeetingList);

  it('puts tasks without a list first, then the own task lists by name, then the Google Tasks lists', () => {
    const loose = item('loose');
    const inSprint = item('in-sprint', { itemListId: 'l-sprint' });
    const inHome = item('in-home', { itemListId: 'l-home' });
    const google = item('google', { isExternal: true, isAllDay: true, googleTaskListId: 'g1', googleTaskListTitle: 'Shopping' });

    const rows = buildRows([google, inSprint, loose, inHome], 'tasks', new Date(), [sprint, home]);

    expect(rows.map((r) => (r.type === 'item' ? r.item.id : r.type === 'group' ? r.title : r.type))).toEqual(['loose', 'Home', 'Sprint', 'Shopping']);
    const sprintRow = rows.find((r) => r.type === 'group' && r.title === 'Sprint');
    expect(sprintRow).toMatchObject({ key: 'list-l-sprint', list: sprint });
    expect(sprintRow?.type === 'group' && sprintRow.items.map((i) => i.id)).toEqual(['in-sprint']);
  });

  it('a list with nothing in it is still shown, so a new list can be seen', () => {
    const rows = buildRows([item('loose')], 'tasks', new Date(), [sprint]);

    expect(rows.map((r) => r.type)).toEqual(['item', 'group']);
    expect(rows[1]).toMatchObject({ title: 'Sprint', items: [] });
  });

  it('an imported Google task moved into an own list leaves its Google group', () => {
    const moved = item('moved', { isExternal: true, isAllDay: true, googleTaskListId: 'g1', googleTaskListTitle: 'Shopping', itemListId: 'l-sprint' });

    const rows = buildRows([moved], 'tasks', new Date(), [sprint]);

    expect(rows.map((r) => (r.type === 'group' ? r.title : r.type))).toEqual(['Sprint']);
  });

  it('events: the ones without a list first (in time order), then the meeting lists; task lists never show on the Events tab', () => {
    const soon = item('soon', { taskType: TaskType.Event, status: TaskStatus.Confirmed, startUtc: inHours(2) });
    const weekly = item('weekly', { taskType: TaskType.Event, status: TaskStatus.Confirmed, startUtc: inHours(30), itemListId: 'l-standups' });

    const rows = buildRows([weekly, soon], 'events', new Date(), [sprint, standups]);

    expect(rows.map((r) => (r.type === 'item' ? r.item.id : r.type === 'group' ? r.title : r.type))).toEqual(['soon', 'Standups']);
  });

  it('the All tab keeps each kind of list inside its own section, and the heading counts the items, not the lists', () => {
    const t1 = item('t1', { itemListId: 'l-sprint' });
    const e1 = item('e1', { taskType: TaskType.Event, status: TaskStatus.Confirmed, startUtc: inHours(2), itemListId: 'l-standups' });

    const rows = buildRows([t1, e1], 'all', new Date(), [sprint, standups]);

    expect(rows.map((r) => (r.type === 'header' ? `${r.title} ${r.count}` : r.type === 'group' ? r.title : r.type))).toEqual([
      'Tasks 1', 'Sprint', 'Events 1', 'Standups',
    ]);
  });

  it('an item pointing at a list that is not loaded is shown like a loose one', () => {
    const rows = buildRows([item('orphan', { itemListId: 'gone' })], 'tasks', new Date(), []);

    expect(rows.map((r) => r.type)).toEqual(['item']);
  });
});
