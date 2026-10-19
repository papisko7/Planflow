export interface TeamDto {
  id: string;
  name: string;
  description: string | null;
  memberCount: number;
  isPersonal: boolean;
  muteNotifications: boolean;
  muteChat: boolean;
}

export interface CreateTeamRequest {
  name: string;
  description: string | null;
}

export enum TeamRole {
  Owner = 0,
  Admin = 1,
  Member = 2,
}

export interface TeamMemberDto {
  id: string;
  userId: string;
  userDisplayName: string;
  role: TeamRole;
  joinedAtUtc: string;
}

export interface AddTeamMemberRequest {
  email: string;
  role: TeamRole;
}

export interface ChangeMemberRoleRequest {
  role: TeamRole;
}
