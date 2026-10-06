// Mirrors PlanFlow.Application.Teams.Common.TeamDto
export interface TeamDto {
  id: string;
  name: string;
  description: string | null;
  memberCount: number;
  // The user's own one-person workspace (created automatically); it holds their personal tasks.
  isPersonal: boolean;
  // The caller's own e-mail mutes for this team (see NotificationSettingsCard).
  muteNotifications: boolean;
  muteChat: boolean;
}

// Mirrors PlanFlow.Api.Contracts.CreateTeamRequest
export interface CreateTeamRequest {
  name: string;
  description: string | null;
}

// Mirrors PlanFlow.Domain.Enums.TeamRole (the API serialises enums as numbers).
export enum TeamRole {
  Owner = 0,
  Admin = 1,
  Member = 2,
}

// Mirrors PlanFlow.Application.Teams.Common.TeamMemberDto
export interface TeamMemberDto {
  id: string;
  userId: string;
  userDisplayName: string;
  role: TeamRole;
  joinedAtUtc: string;
}

// Mirrors PlanFlow.Api.Contracts.AddTeamMemberRequest
export interface AddTeamMemberRequest {
  email: string;
  role: TeamRole;
}

// Mirrors PlanFlow.Api.Contracts.ChangeMemberRoleRequest
export interface ChangeMemberRoleRequest {
  role: TeamRole;
}
