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
  // Filled in by authService; lets the server tell a new device from a known one.
  deviceId?: string;
  deviceName?: string;
}

// Mirrors PlanFlow.Api.Contracts.RegisterRequest
export interface RegisterRequest {
  email: string;
  password: string;
  displayName: string;
  deviceId?: string;
  deviceName?: string;
}

// Mirrors PlanFlow.Api.Contracts.ResetPasswordRequest
export interface ResetPasswordRequest {
  email: string;
  code: string;
  newPassword: string;
}

// Mirrors PlanFlow.Api.Contracts.RefreshRequest
export interface RefreshRequest {
  userId: string;
  refreshToken: string;
}
