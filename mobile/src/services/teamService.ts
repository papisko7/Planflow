import { http } from './httpClient';
import type { CreateTeamRequest, TeamDto } from '../types/team';

export async function getMyTeams(): Promise<TeamDto[]> {
  const { data } = await http.get<TeamDto[]>('/api/teams');
  return data;
}

export async function createTeam(request: CreateTeamRequest): Promise<TeamDto> {
  const { data } = await http.post<TeamDto>('/api/teams', request);
  return data;
}
