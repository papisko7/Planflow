// Mirrors backend PlanFlow.Domain.Enums.TaskStatus — no JsonStringEnumConverter is configured,
// so the API serializes this as a plain int; the numeric order here must stay in sync.
export enum TaskStatus {
  Todo = 0,
  InProgress = 1,
  Blocked = 2,
  Done = 3,
  Cancelled = 4,
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
  impactScore: number;
  blockedByTaskId: string | null;
  manualUrgencyOverride: number | null;
  manualUrgencyOverrideExpiresAtUtc: string | null;
  currentUrgencyScore: number;
  createdAtUtc: string;
  updatedAtUtc: string | null;
}

// Mirrors PlanFlow.Application.Tasks.Common.UrgencyScoreBreakdownDto
export interface UrgencyScoreBreakdownDto {
  deadlineComponent: number;
  aiComponent: number;
  blockingComponent: number;
  impactComponent: number;
  userOverrideComponent: number;
  finalScore: number;
  aiFallbackUsed: boolean;
  createdAtUtc: string;
}

// Mirrors PlanFlow.Application.Tasks.Common.TaskDetailDto
export interface TaskDetailDto {
  task: TaskDto;
  latestScoreBreakdown: UrgencyScoreBreakdownDto | null;
}

// Mirrors PlanFlow.Api.Contracts.CreateTaskRequest
export interface CreateTaskRequest {
  title: string;
  description: string | null;
  assignedUserId: string | null;
  deadlineUtc: string | null;
  impactScore: number;
  blockedByTaskId: string | null;
}

// Mirrors PlanFlow.Api.Contracts.UpdateTaskRequest
export interface UpdateTaskRequest {
  title: string;
  description: string | null;
  status: TaskStatus;
  assignedUserId: string | null;
  deadlineUtc: string | null;
  impactScore: number;
  blockedByTaskId: string | null;
  manualUrgencyOverride: number | null;
  manualUrgencyOverrideExpiresAtUtc: string | null;
}
