import axios from 'axios';
import { API_BASE_URL, setAccessToken, setUnauthorizedHandler } from './httpClient';
import { clearSession, loadSession, saveSession } from './tokenStorage';
import type { AuthResultDto, LoginRequest, RegisterRequest } from '../types/auth';

// Bare instance for the login/register/refresh calls themselves: they must never carry a stale
// Authorization header or trigger the 401 -> refresh interceptor on `http`, which would recurse.
const authHttp = axios.create({ baseURL: API_BASE_URL });

let currentUserId: string | null = null;

export function getCurrentUserId(): string | null {
  return currentUserId;
}

async function persist(result: AuthResultDto): Promise<AuthResultDto> {
  await saveSession({ accessToken: result.accessToken, refreshToken: result.refreshToken, userId: result.userId });
  setAccessToken(result.accessToken);
  currentUserId = result.userId;
  return result;
}

// Tracks which team (if any) the current access token's `team_id` claim was issued for, so
// `ensureTeamContext` can skip a network round-trip when the token already matches.
let currentTeamId: string | null = null;

export function getCurrentTeamId(): string | null {
  return currentTeamId;
}

export async function login(request: LoginRequest): Promise<AuthResultDto> {
  const { data } = await authHttp.post<AuthResultDto>('/api/auth/login', request);
  currentTeamId = request.teamId ?? null;
  return persist(data);
}

export async function register(request: RegisterRequest): Promise<AuthResultDto> {
  const { data } = await authHttp.post<AuthResultDto>('/api/auth/register', request);
  currentTeamId = null;
  return persist(data);
}

export async function refresh(teamId: string | null = currentTeamId): Promise<AuthResultDto> {
  const session = await loadSession();
  if (!session) {
    throw new Error('No stored session to refresh.');
  }
  const { data } = await authHttp.post<AuthResultDto>('/api/auth/refresh', {
    userId: session.userId,
    refreshToken: session.refreshToken,
    teamId,
  });
  currentTeamId = teamId;
  return persist(data);
}

// Team-scoped endpoints (see PlanFlow.Api TasksController) reject requests whose JWT `team_id`
// claim doesn't match the route's teamId, so any call must re-scope the token first if the
// caller has switched teams since the last login/refresh.
export async function ensureTeamContext(teamId: string): Promise<void> {
  if (currentTeamId !== teamId) {
    await refresh(teamId);
  }
}

export async function logout(): Promise<void> {
  await clearSession();
  setAccessToken(null);
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

setUnauthorizedHandler(async () => {
  try {
    const result = await refresh();
    return result.accessToken;
  } catch {
    await logout();
    return null;
  }
});
