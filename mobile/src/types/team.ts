// Mirrors PlanFlow.Application.Teams.Common.TeamDto
export interface TeamDto {
  id: string;
  name: string;
  description: string | null;
  memberCount: number;
}

// Mirrors PlanFlow.Api.Contracts.CreateTeamRequest
export interface CreateTeamRequest {
  name: string;
  description: string | null;
}
