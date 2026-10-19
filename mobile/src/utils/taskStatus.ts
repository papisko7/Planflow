import { TaskStatus, TaskType } from '../types/task';

const TASK_STATUSES = [TaskStatus.Backlog, TaskStatus.InProgress, TaskStatus.Completed, TaskStatus.Cancelled];
const EVENT_STATUSES = [TaskStatus.Confirmed, TaskStatus.Tentative, TaskStatus.Cancelled];

export const STATUS_LABEL: Record<TaskStatus, string> = {
  [TaskStatus.Backlog]: 'Backlog',
  [TaskStatus.InProgress]: 'In progress',
  [TaskStatus.Completed]: 'Completed',
  [TaskStatus.Cancelled]: 'Cancelled',
  [TaskStatus.Confirmed]: 'Confirmed',
  [TaskStatus.Tentative]: 'Tentative',
};

export const statusesFor = (type: TaskType): TaskStatus[] => (type === TaskType.Event ? EVENT_STATUSES : TASK_STATUSES);

export const defaultStatusFor = (type: TaskType): TaskStatus =>
  type === TaskType.Event ? TaskStatus.Confirmed : TaskStatus.Backlog;

export const normalizeStatus = (type: TaskType, status: TaskStatus): TaskStatus =>
  statusesFor(type).includes(status) ? status : defaultStatusFor(type);

export const isFinishedStatus = (status: TaskStatus): boolean =>
  status === TaskStatus.Completed || status === TaskStatus.Cancelled;
