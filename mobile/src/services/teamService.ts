import { http } from './httpClient';
import { invalidateAllTasksCache } from './taskCache';
import type { AddTeamMemberRequest, CreateTeamRequest, TeamDto, TeamMemberDto, TeamRole } from '../types/team';

export async function getMyTeams(): Promise<TeamDto[]> {
  const { data } = await http.get<TeamDto[]>('/api/teams');
  // Personal workspace first, so it is the default target for new tasks.
  return [...data].sort((a, b) => Number(b.isPersonal) - Number(a.isPersonal));
}

export async function createTeam(request: CreateTeamRequest): Promise<TeamDto> {
  const { data } = await http.post<TeamDto>('/api/teams', request);
  invalidateAllTasksCache();
  return data;
}

export async function updateTeam(teamId: string, request: CreateTeamRequest): Promise<TeamDto> {
  const { data } = await http.put<TeamDto>(`/api/teams/${teamId}`, request);
  invalidateAllTasksCache();
  return data;
}

export async function deleteTeam(teamId: string): Promise<void> {
  await http.delete(`/api/teams/${teamId}`);
  invalidateAllTasksCache();
}

export async function getTeamMembers(teamId: string): Promise<TeamMemberDto[]> {
  const { data } = await http.get<TeamMemberDto[]>(`/api/teams/${teamId}/members`);
  return data;
}

export async function addTeamMember(teamId: string, request: AddTeamMemberRequest): Promise<TeamMemberDto> {
  const { data } = await http.post<TeamMemberDto>(`/api/teams/${teamId}/members`, request);
  return data;
}

// Owner/Admin only (the server decides); the member gets an e-mail about the new role.
export async function changeMemberRole(teamId: string, userId: string, role: TeamRole): Promise<TeamMemberDto> {
  const { data } = await http.put<TeamMemberDto>(`/api/teams/${teamId}/members/${userId}/role`, { role });
  invalidateAllTasksCache();
  return data;
}

// Also used to leave a team: pass the caller's own userId.
export async function removeTeamMember(teamId: string, userId: string): Promise<void> {
  await http.delete(`/api/teams/${teamId}/members/${userId}`);
  invalidateAllTasksCache();
}
