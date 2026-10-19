import { type TaskDto } from '../types/task';
import { dueUtc, isPastEvent } from './schedule';
import { isFinishedStatus } from './taskStatus';
import { isInformational } from './templateRules';

// Deterministic local ranking for the dashboard "Do now" card — no API call, so it works
// offline and returns instantly. Tie-break order: urgency score -> earliest deadline (tasks with
// no deadline sort last) -> highest impact -> oldest creation date.
export function selectOneNextAction<T extends TaskDto>(tasks: T[]): T | null {
  // Open work = not finished and not an event that already took place. A task is "blocked" while the task it waits
  // on is still open work (blocked is derived, there is no longer a Blocked status), and a blocked task is not actionable yet.
  // A day off or a birthday (informational) is never "the next action".
  const openIds = new Set(
    tasks.filter((task) => !isFinishedStatus(task.status) && !isPastEvent(task) && !isInformational(task)).map((task) => task.id),
  );
  const candidates = tasks.filter(
    (task) => openIds.has(task.id) && !(task.blockedByTaskId && openIds.has(task.blockedByTaskId)),
  );
  if (candidates.length === 0) return null;

  return candidates.reduce((best, task) => (compareTasks(task, best) < 0 ? task : best));
}

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
