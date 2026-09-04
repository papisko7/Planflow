import { TaskStatus, type TaskDto } from '../types/task';

// Statuses that are no longer actionable "right now" — done, cancelled, or blocked on another task.
const INACTIVE_STATUSES = new Set<TaskStatus>([TaskStatus.Blocked, TaskStatus.Done, TaskStatus.Cancelled]);

// Deterministic local ranking for "One Next Action" focus mode — no LLM/API call, so it works
// offline and returns instantly. Tie-break order: urgency score -> earliest deadline (tasks with
// no deadline sort last) -> highest impact -> oldest creation date.
export function selectOneNextAction(tasks: TaskDto[]): TaskDto | null {
  const candidates = tasks.filter((task) => !INACTIVE_STATUSES.has(task.status));
  if (candidates.length === 0) return null;

  return candidates.reduce((best, task) => (compareTasks(task, best) < 0 ? task : best));
}

// Negative if `a` should rank above `b`.
function compareTasks(a: TaskDto, b: TaskDto): number {
  if (a.currentUrgencyScore !== b.currentUrgencyScore) {
    return b.currentUrgencyScore - a.currentUrgencyScore;
  }

  const aDeadline = deadlineSortValue(a.deadlineUtc);
  const bDeadline = deadlineSortValue(b.deadlineUtc);
  // Subtracting two Infinity values (both tasks with no deadline) would yield NaN, so compare
  // via inequality first rather than relying on the sign of a direct subtraction.
  if (aDeadline !== bDeadline) return aDeadline - bDeadline;

  if (a.impactScore !== b.impactScore) {
    return b.impactScore - a.impactScore;
  }

  return new Date(a.createdAtUtc).getTime() - new Date(b.createdAtUtc).getTime();
}

function deadlineSortValue(deadlineUtc: string | null): number {
  return deadlineUtc ? new Date(deadlineUtc).getTime() : Number.POSITIVE_INFINITY;
}
