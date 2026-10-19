import type { TeamDto } from '../types/team';
import type { ItemListDto } from '../types/itemList';
import type { TaskWithTeam } from '../types/task';

export type AllTasks = { teams: TeamDto[]; tasks: TaskWithTeam[]; lists: ItemListDto[] };

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
