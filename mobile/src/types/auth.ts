export interface AuthResultDto {
  userId: string;
  email: string;
  displayName: string;
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  refreshToken: string;
}

export interface LoginRequest {
  email: string;
  password: string;
  teamId: string | null;
  // Filled in by authService; lets the server tell a new device from a known one.
  deviceId?: string;
  deviceName?: string;
}

export interface RegisterRequest {
  email: string;
  password: string;
  displayName: string;
  deviceId?: string;
  deviceName?: string;
}

export interface ResetPasswordRequest {
  email: string;
  code: string;
  newPassword: string;
}

export interface RefreshRequest {
  userId: string;
  refreshToken: string;
}
