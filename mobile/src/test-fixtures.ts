import { EventTemplate, TaskType, type TaskDto } from './types/task';

// The scheduling/AI part of a TaskDto for a plain Task, so test fixtures only spell out what they care about.
export const PLAIN_TASK_FIELDS = {
  taskType: TaskType.Task,
  startUtc: null,
  endUtc: null,
  estimatedEffortHours: null,
  isRecurring: false,
  recurrenceIntervalHours: null,
  recurrenceEndsAtUtc: null,
  useAiEnhancement: false,
  aiImpactScore: null,
  aiImpactReasoning: null,
  isDataIncomplete: false,
  eventTemplate: EventTemplate.Default,
  location: null,
  colorKey: null,
  isBusy: true,
  isAllDay: false,
  externalSettings: null,
  isExternal: false,
  isDeadlineDefaulted: false,
  googleTaskListId: null,
  googleTaskListTitle: null,
  participantUserIds: [] as string[],
} satisfies Partial<TaskDto>;
