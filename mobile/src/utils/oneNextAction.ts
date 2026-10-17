import { TaskStatus, type TaskDto } from '../types/task';
import { dueUtc, isPastEvent } from './schedule';

// Statuses that are no longer actionable "right now" — done, cancelled, or blocked on another task.
const INACTIVE_STATUSES = new Set<TaskStatus>([TaskStatus.Blocked, TaskStatus.Done, TaskStatus.Cancelled]);

// Deterministic local ranking for the dashboard "Do now" card — no LLM/API call, so it works
// offline and returns instantly. Tie-break order: urgency score -> earliest deadline (tasks with
// no deadline sort last) -> highest impact -> oldest creation date.
export function selectOneNextAction<T extends TaskDto>(tasks: T[]): T | null {
  const candidates = tasks.filter((task) => !INACTIVE_STATUSES.has(task.status) && !isPastEvent(task));
  if (candidates.length === 0) return null;

  return candidates.reduce((best, task) => (compareTasks(task, best) < 0 ? task : best));
}

// Negative if `a` should rank above `b`.
function compareTasks(a: TaskDto, b: TaskDto): number {
  if (a.currentUrgencyScore !== b.currentUrgencyScore) {
    return b.currentUrgencyScore - a.currentUrgencyScore;
  }

  const aDeadline = deadlineSortValue(dueUtc(a));
  const bDeadline = deadlineSortValue(dueUtc(b));
  // Subtracting two Infinity values (both tasks with no deadline) would yield NaN, so compare
  // via inequality first rather than relying on the sign of a direct subtraction.
  if (aDeadline !== bDeadline) return aDeadline - bDeadline;

  if ((a.impactScore ?? 0) !== (b.impactScore ?? 0)) {
    return (b.impactScore ?? 0) - (a.impactScore ?? 0);
  }

  return new Date(a.createdAtUtc).getTime() - new Date(b.createdAtUtc).getTime();
}

function deadlineSortValue(deadlineUtc: string | null): number {
  return deadlineUtc ? new Date(deadlineUtc).getTime() : Number.POSITIVE_INFINITY;
}
