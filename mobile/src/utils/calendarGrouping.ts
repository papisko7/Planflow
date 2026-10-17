import type { TaskDto } from '../types/task';
import { dayKey } from './dateUtils';
import { dueUtc } from './schedule';

export interface AgendaSection<T extends TaskDto = TaskDto> {
  dateKey: string;
  title: string;
  data: T[];
}

// Local calendar day, not UTC midnight — a deadline of 23:00 UTC should still land on "today"
// for a viewer in a positive-offset timezone.
export function dateKey(iso: string): string {
  return dayKey(new Date(iso));
}

function sectionTitle(key: string): string {
  const today = dateKey(new Date().toISOString());
  if (key === today) return 'Today';
  const [y, m, d] = key.split('-').map(Number);
  return new Date(y, m - 1, d).toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' });
}

// Buckets items with a due moment (a Task's deadline, an Event's start) into date-keyed sections sorted chronologically; tasks with no
// deadline can't be placed on a calendar so they're returned separately for their own list.
export function groupTasksByDueDate<T extends TaskDto>(tasks: T[]): { sections: AgendaSection<T>[]; undated: T[] } {
  const buckets = new Map<string, T[]>();
  const undated: T[] = [];

  for (const task of tasks) {
    const due = dueUtc(task);
    if (!due) {
      undated.push(task);
      continue;
    }
    const key = dateKey(due);
    if (!buckets.has(key)) buckets.set(key, []);
    buckets.get(key)!.push(task);
  }

  const sections = [...buckets.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([key, data]) => ({
      dateKey: key,
      title: sectionTitle(key),
      data: data.sort((a, b) => (dueUtc(a) ?? '').localeCompare(dueUtc(b) ?? '')),
    }));

  return { sections, undated };
}
