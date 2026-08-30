import { http } from './httpClient';
import type { TeamDto } from '../types/team';

export async function getMyTeams(): Promise<TeamDto[]> {
  const { data } = await http.get<TeamDto[]>('/api/teams');
  return data;
}
