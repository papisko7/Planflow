import { ItemListKind, type ItemListDto } from '../types/itemList';
import { TaskType, type TaskDto } from '../types/task';
import { isPastEvent } from './schedule';

export type ItemTab = 'all' | 'tasks' | 'events';

export type ItemRow<T extends TaskDto> =
  | { type: 'header'; key: string; title: string; count: number }
  | { type: 'item'; key: string; item: T }
  | { type: 'group'; key: string; title: string; items: T[]; list?: ItemListDto };

const isGoogleTask = (t: TaskDto) => t.taskType !== TaskType.Event && t.isExternal;

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

export function filterByTab<T extends TaskDto>(items: T[], tab: ItemTab, now: Date = new Date()): T[] {
  if (tab === 'all') return [...filterByTab(items, 'tasks', now), ...filterByTab(items, 'events', now)];
  if (tab === 'tasks') return items.filter((t) => t.taskType !== TaskType.Event);

  const start = (t: T) => new Date(t.startUtc ?? t.createdAtUtc).getTime();
  const events = items.filter((t) => t.taskType === TaskType.Event);
  const upcoming = events.filter((t) => !isPastEvent(t, now)).sort((a, b) => start(a) - start(b));
  const past = events.filter((t) => isPastEvent(t, now)).sort((a, b) => start(b) - start(a));
  return [...upcoming, ...past];
}

function ownListGroups<T extends TaskDto>(items: T[], lists: ItemListDto[], kind: ItemListKind): ItemRow<T>[] {
  return lists
    .filter((l) => l.kind === kind)
    .sort((a, b) => a.name.localeCompare(b.name))
    .map((list) => ({ type: 'group', key: `list-${list.id}`, title: list.name, items: items.filter((i) => i.itemListId === list.id), list }));
}

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
