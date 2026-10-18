import type { TeamDto } from '../types/team';
import type { ItemListDto } from '../types/itemList';
import type { TaskWithTeam } from '../types/task';

export type AllTasks = { teams: TeamDto[]; tasks: TaskWithTeam[]; lists: ItemListDto[] };

// Short-lived cache of "all my tasks" shared by the Dashboard/Tasks/Calendar tabs (see getAllTasks).
// Lives in its own module so teamService and taskService can both invalidate it without importing each other.
export const CACHE_TTL_MS = 15_000;

let cache: { at: number; value: AllTasks } | null = null;

export function getCachedAllTasks(): AllTasks | null {
  return cache && Date.now() - cache.at < CACHE_TTL_MS ? cache.value : null;
}

export function setCachedAllTasks(value: AllTasks): void {
  cache = { at: Date.now(), value };
}

export function invalidateAllTasksCache(): void {
  cache = null;
}
