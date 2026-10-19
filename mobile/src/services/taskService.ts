import { http } from './httpClient';
import { runInTeamContext } from './authService';
import { getMyTeams } from './teamService';
import { getCachedAllTasks, invalidateAllTasksCache, setCachedAllTasks, type AllTasks } from './taskCache';
import type { BulkResult, ItemListDto } from '../types/itemList';
import type { CreateTaskRequest, SeriesScope, TaskDetailDto, TaskDto, TaskStatus, TaskWithTeam, UpdateTaskRequest } from '../types/task';

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
    const lists: ItemListDto[] = [];
    for (const team of teams) {
      const teamData = await runInTeamContext(team.id, async () => {
        const { data: teamTasks } = await http.get<TaskDto[]>(`/api/teams/${team.id}/tasks`);
        const teamLists = await http.get<ItemListDto[]>(`/api/teams/${team.id}/lists`).then((r) => r.data).catch(() => [] as ItemListDto[]);
        return { teamTasks, teamLists };
      });
      tasks.push(...teamData.teamTasks.map((t) => ({ ...t, teamName: team.name })));
      lists.push(...teamData.teamLists);
    }
    const value = { teams, tasks, lists };
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

export function leaveTask(taskId: string, teamId: string): Promise<void> {
  return runInTeamContext(teamId, async () => {
    await http.delete(`/api/tasks/${taskId}/participants/me`);
    invalidateAllTasksCache();
  });
}

export function deleteTask(taskId: string, teamId: string, scope?: SeriesScope): Promise<void> {
  return runInTeamContext(teamId, async () => {
    await http.delete(`/api/tasks/${taskId}`, scope === undefined ? undefined : { params: { scope } });
    invalidateAllTasksCache();
  });
}

const BULK_CHUNK = 1000;

async function bulk(teamId: string, action: string, ids: string[], extra: object): Promise<BulkResult> {
  return runInTeamContext(teamId, async () => {
    const total: BulkResult = { succeeded: 0, skippedIds: [] };
    try {
      for (let i = 0; i < ids.length; i += BULK_CHUNK) {
        const { data } = await http.post<BulkResult>(`/api/teams/${teamId}/tasks/${action}`, { taskIds: ids.slice(i, i + BULK_CHUNK), ...extra });
        total.succeeded += data.succeeded;
        total.skippedIds.push(...data.skippedIds);
      }
    } finally {
      invalidateAllTasksCache();
    }
    return total;
  });
}

export const bulkDeleteTasks = (teamId: string, ids: string[]): Promise<BulkResult> => bulk(teamId, 'bulk-delete', ids, {});

export const bulkSetStatus = (teamId: string, ids: string[], status: TaskStatus): Promise<BulkResult> =>
  bulk(teamId, 'bulk-status', ids, { status });

export const bulkMoveToList = (teamId: string, ids: string[], listId: string | null): Promise<BulkResult> =>
  bulk(teamId, 'bulk-move', ids, { itemListId: listId });
