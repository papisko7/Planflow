import { http } from './httpClient';
import { runInTeamContext } from './authService';
import { getMyTeams } from './teamService';
import { getCachedAllTasks, invalidateAllTasksCache, setCachedAllTasks, type AllTasks } from './taskCache';
import type { CreateTaskRequest, TaskDetailDto, TaskDto, TaskWithTeam, UpdateTaskRequest } from '../types/task';

export function getTeamTasks(teamId: string): Promise<TaskDto[]> {
  return runInTeamContext(teamId, async () => {
    const { data } = await http.get<TaskDto[]>(`/api/teams/${teamId}/tasks`);
    return data;
  });
}

// The API is team-scoped (one team per token), so "all my tasks" is one request per team, plus a
// token refresh each time the team changes. Dashboard/Tasks/Calendar all want this on every focus,
// so the result is cached briefly and shared (one in-flight request for all three tabs). Any
// mutation calls invalidateAllTasksCache(), so changes still show up immediately.
let inFlight: Promise<AllTasks> | null = null;

export async function getAllTasks(force = false): Promise<AllTasks> {
  if (!force) {
    const cached = getCachedAllTasks();
    if (cached) return cached;
  }
  if (inFlight) return inFlight;

  inFlight = (async () => {
    const teams = await getMyTeams();
    const tasks: TaskWithTeam[] = [];
    for (const team of teams) {
      const teamTasks = await getTeamTasks(team.id);
      tasks.push(...teamTasks.map((t) => ({ ...t, teamName: team.name })));
    }
    const value = { teams, tasks };
    setCachedAllTasks(value);
    return value;
  })().finally(() => {
    inFlight = null;
  });
  return inFlight;
}

export function createTask(teamId: string, request: CreateTaskRequest): Promise<TaskDto> {
  return runInTeamContext(teamId, async () => {
    const { data } = await http.post<TaskDto>(`/api/teams/${teamId}/tasks`, request);
    invalidateAllTasksCache();
    return data;
  });
}

// Single-task routes are authorized against the token's team, so they need the task's team too.
export function getTask(taskId: string, teamId: string): Promise<TaskDetailDto> {
  return runInTeamContext(teamId, async () => {
    const { data } = await http.get<TaskDetailDto>(`/api/tasks/${taskId}`);
    return data;
  });
}

export function updateTask(taskId: string, teamId: string, request: UpdateTaskRequest): Promise<TaskDto> {
  return runInTeamContext(teamId, async () => {
    const { data } = await http.put<TaskDto>(`/api/tasks/${taskId}`, request);
    invalidateAllTasksCache();
    return data;
  });
}

// A guest of an event / co-contractor of a task steps out of it (only ever their own participation).
export function leaveTask(taskId: string, teamId: string): Promise<void> {
  return runInTeamContext(teamId, async () => {
    await http.delete(`/api/tasks/${taskId}/participants/me`);
    invalidateAllTasksCache();
  });
}

export function deleteTask(taskId: string, teamId: string): Promise<void> {
  return runInTeamContext(teamId, async () => {
    await http.delete(`/api/tasks/${taskId}`);
    invalidateAllTasksCache();
  });
}
