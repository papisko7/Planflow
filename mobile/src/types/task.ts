// Mirrors backend PlanFlow.Domain.Enums.TaskStatus — no JsonStringEnumConverter is configured,
// so the API serializes this as a plain int; the numeric order here must stay in sync.
export enum TaskStatus {
  Todo = 0,
  InProgress = 1,
  Blocked = 2,
  Done = 3,
  Cancelled = 4,
}

// Mirrors backend PlanFlow.Domain.Enums.TaskType (plain int on the wire): a Task is work with a deadline,
// an Event happens in a time range.
export enum TaskType {
  Task = 0,
  Event = 1,
}

// Mirrors PlanFlow.Application.Tasks.Common.TaskDto
export interface TaskDto {
  id: string;
  teamId: string;
  assignedUserId: string | null;
  title: string;
  description: string | null;
  status: TaskStatus;
  deadlineUtc: string | null;
  impactScore: number | null;
  blockedByTaskId: string | null;
  manualUrgencyOverride: number | null;
  manualUrgencyOverrideExpiresAtUtc: string | null;
  currentUrgencyScore: number;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  taskType: TaskType;
  startUtc: string | null; // Event only
  endUtc: string | null; // Event only
  estimatedEffortHours: number | null;
  isRecurring: boolean;
  recurrenceIntervalHours: number | null;
  recurrenceEndsAtUtc: string | null;
  useAiEnhancement: boolean;
  aiImpactScore: number | null;
  aiImpactReasoning: string | null;
  isDataIncomplete: boolean;
  // Latest "[Priority] why" sentence; only the team list endpoint fills it.
  reasonText?: string | null;
}

// Mirrors PlanFlow.Application.Tasks.Common.UrgencyScoreBreakdownDto
export interface UrgencyScoreBreakdownDto {
  deadlineComponent: number;
  blockingComponent: number;
  impactComponent: number | null; // null = task not rated, its weight is shared by the other factors
  userOverrideComponent: number;
  finalScore: number;
  isBlocked: boolean; // true = waiting on an unfinished blocker, score is halved
  createdAtUtc: string;
  reasonText?: string | null; // null on score rows written before the explanation existed
}

// Mirrors PlanFlow.Application.Tasks.Common.TaskDetailDto
export interface TaskDetailDto {
  task: TaskDto;
  latestScoreBreakdown: UrgencyScoreBreakdownDto | null;
}

// The scheduling fields shared by the create and update requests (type, dates, effort, recurrence).
export interface ScheduleFields {
  taskType: TaskType;
  deadlineUtc: string | null; // Task only
  startUtc: string | null; // Event only
  endUtc: string | null; // Event only
  estimatedEffortHours: number | null;
  isRecurring: boolean;
  recurrenceIntervalHours: number | null;
  recurrenceEndsAtUtc: string | null;
}

// Mirrors PlanFlow.Api.Contracts.CreateTaskRequest
export interface CreateTaskRequest extends ScheduleFields {
  title: string;
  description: string | null;
  assignedUserId: string | null;
  impactScore: number | null;
  blockedByTaskId: string | null;
}

// Mirrors PlanFlow.Api.Contracts.UpdateTaskRequest
export interface UpdateTaskRequest extends ScheduleFields {
  title: string;
  description: string | null;
  status: TaskStatus;
  assignedUserId: string | null;
  impactScore: number | null;
  blockedByTaskId: string | null;
  manualUrgencyOverride: number | null;
  manualUrgencyOverrideExpiresAtUtc: string | null;
  useAiEnhancement: boolean;
}

// A task plus the name of its team (resolved client-side from the teams list) for display.
export type TaskWithTeam = TaskDto & { teamName: string };
