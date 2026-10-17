import axios from 'axios';
import { Platform } from 'react-native';
import { API_BASE_URL, getAccessToken, setAccessToken, setUnauthorizedHandler } from './httpClient';
import { clearAccessToken, clearSession, getDeviceId, loadRefreshCredentials, loadRefreshCredentialsForUser, loadSession, saveSession } from './tokenStorage';
import type { AuthResultDto, LoginRequest, RegisterRequest, ResetPasswordRequest } from '../types/auth';

// Bare instance for the login/register/refresh calls themselves: they must never carry a stale
// Authorization header or trigger the 401 -> refresh interceptor on `http`, which would recurse.
const authHttp = axios.create({ baseURL: API_BASE_URL });

let currentUserId: string | null = null;

export function getCurrentUserId(): string | null {
  return currentUserId;
}

// When the in-memory access token stops being valid (0 = unknown, e.g. right after an app restart).
let accessExpiresAtMs = 0;

// Refresh a minute early so a token never expires between "we checked" and "the server read it".
function isAccessTokenFresh(): boolean {
  return Date.now() < accessExpiresAtMs - 60_000;
}

async function persist(result: AuthResultDto): Promise<AuthResultDto> {
  await saveSession({ accessToken: result.accessToken, refreshToken: result.refreshToken, userId: result.userId });
  setAccessToken(result.accessToken);
  accessExpiresAtMs = Date.parse(result.accessTokenExpiresAtUtc) || 0;
  currentUserId = result.userId;
  return result;
}

// Tracks which team (if any) the current access token's `team_id` claim was issued for, so
// `ensureTeamContext` can skip a network round-trip when the token already matches.
let currentTeamId: string | null = null;

export function getCurrentTeamId(): string | null {
  return currentTeamId;
}

// Shown in the "new sign-in" e-mail, e.g. "PlanFlow on Android".
function deviceName(): string {
  const platform = Platform.OS === 'ios' ? 'iPhone / iPad' : Platform.OS === 'android' ? 'Android' : Platform.OS;
  return `PlanFlow on ${platform}`;
}

export async function identifyByEmail(email: string): Promise<{ userId: string }> {
  const { data } = await authHttp.post<{ userId: string }>('/api/auth/identify', { email });
  return data;
}

export async function login(request: LoginRequest): Promise<AuthResultDto> {
  const body = { ...request, deviceId: await getDeviceId(), deviceName: deviceName() };
  const { data } = await authHttp.post<AuthResultDto>('/api/auth/login', body);
  currentTeamId = request.teamId ?? null;
  return persist(data);
}

export async function register(request: RegisterRequest): Promise<AuthResultDto> {
  const body = { ...request, deviceId: await getDeviceId(), deviceName: deviceName() };
  const { data } = await authHttp.post<AuthResultDto>('/api/auth/register', body);
  currentTeamId = null;
  return persist(data);
}

// Password reset: the server e-mails a 6-digit code (always answers 204, so it never reveals whether the address has an account),
// then the code + a new password complete the reset and sign the account out everywhere.
export async function requestPasswordReset(email: string): Promise<void> {
  await authHttp.post('/api/auth/forgot-password', { email });
}

export async function resetPassword(request: ResetPasswordRequest): Promise<void> {
  await authHttp.post('/api/auth/reset-password', request);
}

export async function refresh(teamId: string | null = currentTeamId): Promise<AuthResultDto> {
  // loadRefreshCredentials() only needs refreshToken + userId, so it works even after a soft
  // logout where the access token has been cleared from SecureStore.
  const creds = await loadRefreshCredentials();
  if (!creds) {
    throw new Error('No stored session to refresh.');
  }
  const { data } = await authHttp.post<AuthResultDto>('/api/auth/refresh', {
    userId: creds.userId,
    refreshToken: creds.refreshToken,
    teamId,
  });
  currentTeamId = teamId;
  return persist(data);
}

export async function refreshAsUser(userId: string, teamId: string | null = null): Promise<AuthResultDto> {
  const creds = await loadRefreshCredentialsForUser(userId);
  if (!creds) {
    throw new Error('No stored session for this user.');
  }
  const { data } = await authHttp.post<AuthResultDto>('/api/auth/refresh', {
    userId: creds.userId,
    refreshToken: creds.refreshToken,
    teamId,
  });
  currentTeamId = teamId;
  return persist(data);
}

// Team-scoped endpoints (see PlanFlow.Api TasksController) reject requests whose JWT `team_id`
// claim doesn't match the route's teamId, so any call must re-scope the token first if the
// caller has switched teams since the last login/refresh.
// It also refreshes when the token has (nearly) expired: SignalR handshakes bypass the axios 401
// interceptor, so an expired token there would otherwise be sent as-is and rejected with 401.
export async function ensureTeamContext(teamId: string): Promise<void> {
  if (currentTeamId !== teamId || !isAccessTokenFresh()) {
    await refreshOrExpire(teamId);
  }
}

// The backend rotates the refresh token on every refresh (single use) and the app keeps ONE
// team-scoped access token. Several screens fetch in parallel (Dashboard, Tasks, Teams, Chat), so
// without a queue two refreshes reuse the same refresh token (second one is rejected) or one screen
// swaps the token's team while another's request is still in flight. This runs "scope token to
// team + do the call" one at a time.
let teamContextQueue: Promise<unknown> = Promise.resolve();

export function runInTeamContext<T>(teamId: string, action: () => Promise<T>): Promise<T> {
  const run = teamContextQueue.then(async () => {
    await ensureTeamContext(teamId);
    return action();
  });
  teamContextQueue = run.catch(() => undefined);
  return run;
}

export async function logout(): Promise<void> {
  await clearSession();
  setAccessToken(null);
  accessExpiresAtMs = 0;
  currentTeamId = null;
}

// Used when PIN login is enabled: wipes the in-memory session and the access token from
// SecureStore, but keeps the refresh token + userId so PIN can exchange them for a new
// access token on next login without requiring a password.
export async function softLogout(): Promise<void> {
  await clearAccessToken();
  setAccessToken(null);
  accessExpiresAtMs = 0;
  currentTeamId = null;
}

// Call once at app startup: restores a previous session optimistically (an expired access
// token still triggers the normal 401 -> refresh flow on the first authenticated request).
export async function restoreSession(): Promise<boolean> {
  const session = await loadSession();
  if (!session) {
    return false;
  }
  setAccessToken(session.accessToken);
  currentUserId = session.userId;
  return true;
}

// SessionProvider registers this so a dead session sends the user to the login screen
// instead of leaving every tab stuck on a "401" error.
let onSessionExpired: (() => void) | null = null;

export function setSessionExpiredHandler(handler: (() => void) | null): void {
  onSessionExpired = handler;
}

// Only a 401 from /refresh means the session is really dead (token revoked/expired); a network
// blip or server error must not log the user out, so those are just rethrown.
async function refreshOrExpire(teamId: string | null): Promise<string> {
  try {
    return (await refresh(teamId)).accessToken;
  } catch (error) {
    if (axios.isAxiosError(error) && error.response?.status === 401) {
      await logout();
      onSessionExpired?.();
    }
    throw error;
  }
}

// Several requests can hit 401 at the same moment (Dashboard + Tasks + Calendar load together).
// Refresh tokens are single use, so they must share ONE refresh call - a second call would reuse
// the already-rotated token, fail, and wrongly log the user out.
let refreshInFlight: Promise<string | null> | null = null;

function refreshShared(): Promise<string | null> {
  refreshInFlight ??= refreshOrExpire(currentTeamId)
    .catch(() => null)
    .finally(() => {
      refreshInFlight = null;
    });
  return refreshInFlight;
}

setUnauthorizedHandler(refreshShared);

// For SignalR's `accessTokenFactory`, which runs on every (re)connect handshake: hands out a token
// that is still valid, refreshing first if the current one has expired.
export async function getFreshAccessToken(): Promise<string> {
  if (!isAccessTokenFresh()) {
    await refreshShared();
  }
  return getAccessToken() ?? '';
}
