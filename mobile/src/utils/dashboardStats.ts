import { TaskStatus, type TaskDto } from '../types/task';

export interface DashboardStats {
  open: number;
  overdue: number;
  dueToday: number;
  done: number;
  // Done + still open; cancelled tasks are left out so "8/10 done" is not diluted by dropped work.
  total: number;
}

const isOpen = (t: TaskDto) => t.status !== TaskStatus.Done && t.status !== TaskStatus.Cancelled;

function sameLocalDay(a: Date, b: Date): boolean {
  return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
}

// The four headline numbers on the dashboard. "Overdue" = open and past its deadline;
// "due today" = open with a deadline later today (so the two never overlap).
export function computeDashboardStats(tasks: TaskDto[], now: Date = new Date()): DashboardStats {
  let open = 0;
  let overdue = 0;
  let dueToday = 0;
  let done = 0;

  for (const task of tasks) {
    if (task.status === TaskStatus.Done) {
      done++;
      continue;
    }
    if (!isOpen(task)) continue;
    open++;
    if (!task.deadlineUtc) continue;
    const deadline = new Date(task.deadlineUtc);
    if (deadline.getTime() < now.getTime()) overdue++;
    else if (sameLocalDay(deadline, now)) dueToday++;
  }

  return { open, overdue, dueToday, done, total: open + done };
}

// "just now", "5 min ago", "3 h ago", "2 d ago" — for the last-sync / last-refresh hints.
export function formatAgo(iso: string | Date, now: Date = new Date()): string {
  const diffMin = Math.max(0, Math.round((now.getTime() - new Date(iso).getTime()) / 60_000));
  if (diffMin < 1) return 'just now';
  if (diffMin < 60) return `${diffMin} min ago`;
  const hours = Math.round(diffMin / 60);
  if (hours < 24) return `${hours} h ago`;
  return `${Math.round(hours / 24)} d ago`;
}
