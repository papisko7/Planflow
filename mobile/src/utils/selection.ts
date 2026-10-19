import { TaskType, type TaskDto, type TaskStatus } from '../types/task';
import { ItemListKind } from '../types/itemList';
import { statusesFor } from './taskStatus';

// The statuses that fit EVERY ticked item: a task has Backlog / In progress / Completed / Cancelled, an event Confirmed / Tentative /
// Cancelled, so a mixed selection can only be cancelled together. Order is the first item's order.
export function commonStatuses(items: Pick<TaskDto, 'taskType'>[]): TaskStatus[] {
  if (items.length === 0) return [];
  const lists = items.map((item) => statusesFor(item.taskType));
  return lists[0].filter((status) => lists.every((list) => list.includes(status)));
}

// A list entry that stands for a whole series (see collapseSeries) acts for all its occurrences when it is ticked, so deleting or cancelling
// "the weekly meeting" does not leave next week's copy behind. An entry that is a single occurrence (the Agenda) acts for itself only.
export function expandToSeries<T extends TaskDto>(selected: T[], all: T[]): T[] {
  const seriesIds = new Set(selected.filter((t) => (t.seriesMore ?? 0) > 0 && t.seriesId).map((t) => t.seriesId!));
  if (seriesIds.size === 0) return selected;
  const selectedIds = new Set(selected.map((t) => t.id));
  return all.filter((t) => selectedIds.has(t.id) || (t.taskType === TaskType.Event && !!t.seriesId && seriesIds.has(t.seriesId)));
}

export function listKindFor(items: Pick<TaskDto, 'taskType'>[]): ItemListKind | null {
  if (items.length === 0) return null;
  const events = items.filter((t) => t.taskType === TaskType.Event).length;
  if (events === 0) return ItemListKind.TaskList;
  return events === items.length ? ItemListKind.MeetingList : null;
}

export function groupByTeam<T extends Pick<TaskDto, 'teamId'>>(items: T[]): Map<string, T[]> {
  const groups = new Map<string, T[]>();
  for (const item of items) {
    const group = groups.get(item.teamId);
    if (group) group.push(item);
    else groups.set(item.teamId, [item]);
  }
  return groups;
}
