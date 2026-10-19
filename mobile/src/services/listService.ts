import { http } from './httpClient';
import { runInTeamContext } from './authService';
import { invalidateAllTasksCache } from './taskCache';
import type { ItemListDto, ItemListKind } from '../types/itemList';

export function getTeamLists(teamId: string): Promise<ItemListDto[]> {
  return runInTeamContext(teamId, async () => {
    const { data } = await http.get<ItemListDto[]>(`/api/teams/${teamId}/lists`);
    return data;
  });
}

export function createList(teamId: string, name: string, kind: ItemListKind): Promise<ItemListDto> {
  return runInTeamContext(teamId, async () => {
    const { data } = await http.post<ItemListDto>(`/api/teams/${teamId}/lists`, { name, kind });
    invalidateAllTasksCache();
    return data;
  });
}

export function renameList(teamId: string, listId: string, name: string): Promise<ItemListDto> {
  return runInTeamContext(teamId, async () => {
    const { data } = await http.put<ItemListDto>(`/api/teams/${teamId}/lists/${listId}`, { name });
    invalidateAllTasksCache();
    return data;
  });
}

export function deleteList(teamId: string, listId: string): Promise<void> {
  return runInTeamContext(teamId, async () => {
    await http.delete(`/api/teams/${teamId}/lists/${listId}`);
    invalidateAllTasksCache();
  });
}
