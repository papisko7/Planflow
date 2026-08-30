import type { TaskDto } from '../types/task';

export interface AgendaSection {
  dateKey: string;
  title: string;
  data: TaskDto[];
}

// Local calendar day, not UTC midnight — a deadline of 23:00 UTC should still land on "today"
// for a viewer in a positive-offset timezone.
function dateKey(iso: string): string {
  const d = new Date(iso);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

function sectionTitle(key: string): string {
  const today = dateKey(new Date().toISOString());
  if (key === today) return 'Today';
  const [y, m, d] = key.split('-').map(Number);
  return new Date(y, m - 1, d).toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' });
}

// Buckets tasks with a deadline into date-keyed sections sorted chronologically; tasks with no
// deadline can't be placed on a calendar so they're returned separately for their own list.
export function groupTasksByDueDate(tasks: TaskDto[]): { sections: AgendaSection[]; undated: TaskDto[] } {
  const buckets = new Map<string, TaskDto[]>();
  const undated: TaskDto[] = [];

  for (const task of tasks) {
    if (!task.deadlineUtc) {
      undated.push(task);
      continue;
    }
    const key = dateKey(task.deadlineUtc);
    if (!buckets.has(key)) buckets.set(key, []);
    buckets.get(key)!.push(task);
  }

  const sections = [...buckets.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([key, data]) => ({
      dateKey: key,
      title: sectionTitle(key),
      data: data.sort((a, b) => (a.deadlineUtc ?? '').localeCompare(b.deadlineUtc ?? '')),
    }));

  return { sections, undated };
}
