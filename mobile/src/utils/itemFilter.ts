import { TaskType, type TaskDto } from '../types/task';
import { isPastEvent } from './schedule';

export type ItemTab = 'tasks' | 'events';

// Splits the unified list for the Tasks / Events tabs and orders each the way people read it:
// tasks keep the server order (most urgent first); events are chronological - what is still ahead
// (or happening now) first, soonest on top, then the ones that already took place, newest first.
export function filterByTab<T extends TaskDto>(items: T[], tab: ItemTab, now: Date = new Date()): T[] {
  if (tab === 'tasks') return items.filter((t) => t.taskType !== TaskType.Event);

  const start = (t: T) => new Date(t.startUtc ?? t.createdAtUtc).getTime();
  const events = items.filter((t) => t.taskType === TaskType.Event);
  const upcoming = events.filter((t) => !isPastEvent(t, now)).sort((a, b) => start(a) - start(b));
  const past = events.filter((t) => isPastEvent(t, now)).sort((a, b) => start(b) - start(a));
  return [...upcoming, ...past];
}

export function countByTab(items: TaskDto[]): Record<ItemTab, number> {
  const events = items.filter((t) => t.taskType === TaskType.Event).length;
  return { tasks: items.length - events, events };
}
