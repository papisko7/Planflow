import { TaskStatus, TaskType, type TaskDto } from '../types/task';
import type { MyAbsence } from '../types/user';
import { dueMoment, utcDay } from './schedule';

// True when an open task of the person falls due while they are out of office: the app then says so on the card and in the details.
// A whole-day deadline counts from the start of that day, so an absence that covers any part of the due day is a warning.
export function deadlineInAbsence(
  task: Pick<TaskDto, 'taskType' | 'status' | 'deadlineUtc' | 'startUtc' | 'isAllDay'>,
  absences: readonly MyAbsence[],
  now: Date = new Date(),
): boolean {
  if (task.taskType !== TaskType.Task || task.status === TaskStatus.Completed || task.status === TaskStatus.Cancelled) return false;
  const due = dueMoment(task);
  if (!due || due.getTime() <= now.getTime()) return false;
  const from = task.isAllDay && task.deadlineUtc ? utcDay(task.deadlineUtc) : due;
  return absences.some((a) => new Date(a.startUtc).getTime() <= due.getTime() && new Date(a.endUtc).getTime() > from.getTime());
}

export const DEADLINE_IN_ABSENCE_TEXT = 'Deadline falls during your time off';
