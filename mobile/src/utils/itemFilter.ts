import { ItemListKind, type ItemListDto } from '../types/itemList';
import { TaskType, type TaskDto } from '../types/task';
import { isPastEvent } from './schedule';

export type ItemTab = 'all' | 'tasks' | 'events';

// One line of the Items list: a section heading, an item card, or a collapsible group of cards: a Google Tasks list (read-only mirror) or,
// when `list` is set, one of the team's own lists (it can be renamed or deleted, and may be empty).
export type ItemRow<T extends TaskDto> =
  | { type: 'header'; key: string; title: string; count: number }
  | { type: 'item'; key: string; item: T }
  | { type: 'group'; key: string; title: string; items: T[]; list?: ItemListDto };

// A task imported from Google Tasks. Such tasks are not mixed in with your own: each Google list becomes one group.
const isGoogleTask = (t: TaskDto) => t.taskType !== TaskType.Event && t.isExternal;

// The tasks of the Google lists, one group per list (alphabetical by list name; the tasks inside keep the urgency order they came in).
// A task whose list is gone from Google keeps a generic "Google Tasks" group.
function groupByGoogleList<T extends TaskDto>(tasks: T[]): ItemRow<T>[] {
  const groups = new Map<string, { title: string; items: T[] }>();
  for (const task of tasks) {
    const key = task.googleTaskListId ?? 'unlisted';
    if (!groups.has(key)) groups.set(key, { title: task.googleTaskListTitle ?? 'Google Tasks', items: [] });
    groups.get(key)!.items.push(task);
  }
  return [...groups.entries()]
    .sort(([, a], [, b]) => a.title.localeCompare(b.title))
    .map(([key, group]) => ({ type: 'group', key: `google-list-${key}`, title: group.title, items: group.items }));
}

// Splits the unified list for the All / Tasks / Events tabs and orders each part the way people read it:
// tasks keep the server order (most urgent first); events are chronological - what is still ahead
// (or happening now) first, soonest on top, then the ones that already took place, newest first.
export function filterByTab<T extends TaskDto>(items: T[], tab: ItemTab, now: Date = new Date()): T[] {
  if (tab === 'all') return [...filterByTab(items, 'tasks', now), ...filterByTab(items, 'events', now)];
  if (tab === 'tasks') return items.filter((t) => t.taskType !== TaskType.Event);

  const start = (t: T) => new Date(t.startUtc ?? t.createdAtUtc).getTime();
  const events = items.filter((t) => t.taskType === TaskType.Event);
  const upcoming = events.filter((t) => !isPastEvent(t, now)).sort((a, b) => start(a) - start(b));
  const past = events.filter((t) => isPastEvent(t, now)).sort((a, b) => start(b) - start(a));
  return [...upcoming, ...past];
}

// The own lists of one kind as groups (alphabetical; an empty list is still shown, so a new list can be seen before it has anything in it).
function ownListGroups<T extends TaskDto>(items: T[], lists: ItemListDto[], kind: ItemListKind): ItemRow<T>[] {
  return lists
    .filter((l) => l.kind === kind)
    .sort((a, b) => a.name.localeCompare(b.name))
    .map((list) => ({ type: 'group', key: `list-${list.id}`, title: list.name, items: items.filter((i) => i.itemListId === list.id), list }));
}

// The rows to render. "All" is NOT one mixed list: it shows a Tasks section and an Events section, each under its own
// heading (an empty one is left out). Tasks and Events tabs are plain lists. In the task part your tasks without a list come first, then
// one collapsed group per own task list, then one per Google Tasks list. Events without a list come first, then the own meeting lists.
export function buildRows<T extends TaskDto>(items: T[], tab: ItemTab, now: Date = new Date(), lists: ItemListDto[] = []): ItemRow<T>[] {
  const listIds = new Set(lists.map((l) => l.id));
  const inOwnList = (t: T) => !!t.itemListId && listIds.has(t.itemListId);
  const toRows = (list: T[]): ItemRow<T>[] => list.map((item) => ({ type: 'item', key: item.id, item }));
  const taskRows = (list: T[]): ItemRow<T>[] => [
    ...toRows(list.filter((t) => !inOwnList(t) && !isGoogleTask(t))),
    ...ownListGroups(list, lists, ItemListKind.TaskList),
    ...groupByGoogleList(list.filter((t) => !inOwnList(t) && isGoogleTask(t))),
  ];
  const eventRows = (list: T[]): ItemRow<T>[] => [...toRows(list.filter((t) => !inOwnList(t))), ...ownListGroups(list, lists, ItemListKind.MeetingList)];
  if (tab === 'events') return eventRows(filterByTab(items, tab, now));
  if (tab === 'tasks') return taskRows(filterByTab(items, tab, now));

  const sections: ItemRow<T>[] = [];
  for (const [key, title] of [['tasks', 'Tasks'], ['events', 'Events']] as const) {
    const list = filterByTab(items, key, now);
    const rows = key === 'tasks' ? taskRows(list) : eventRows(list);
    if (rows.length > 0) sections.push({ type: 'header', key: `header-${key}`, title, count: list.length }, ...rows);
  }
  return sections;
}

export function countByTab(items: TaskDto[]): Record<ItemTab, number> {
  const events = items.filter((t) => t.taskType === TaskType.Event).length;
  return { all: items.length, tasks: items.length - events, events };
}
