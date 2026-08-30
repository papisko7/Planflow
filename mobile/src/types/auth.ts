// Mirrors PlanFlow.Application.Auth.Common.AuthResultDto
export interface AuthResultDto {
  userId: string;
  email: string;
  displayName: string;
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  refreshToken: string;
}

// Mirrors PlanFlow.Api.Contracts.LoginRequest
export interface LoginRequest {
  email: string;
  password: string;
  teamId: string | null;
}

// Mirrors PlanFlow.Api.Contracts.RegisterRequest
export interface RegisterRequest {
  email: string;
  password: string;
  displayName: string;
}

// Mirrors PlanFlow.Api.Contracts.RefreshRequest
export interface RefreshRequest {
  userId: string;
  refreshToken: string;
}
