import axios from 'axios';
import { API_BASE_URL, setAccessToken, setUnauthorizedHandler } from './httpClient';
import { clearSession, loadSession, saveSession } from './tokenStorage';
import type { AuthResultDto, LoginRequest, RegisterRequest } from '../types/auth';

// Bare instance for the login/register/refresh calls themselves: they must never carry a stale
// Authorization header or trigger the 401 -> refresh interceptor on `http`, which would recurse.
const authHttp = axios.create({ baseURL: API_BASE_URL });

async function persist(result: AuthResultDto): Promise<AuthResultDto> {
  await saveSession({ accessToken: result.accessToken, refreshToken: result.refreshToken, userId: result.userId });
  setAccessToken(result.accessToken);
  return result;
}

export async function login(request: LoginRequest): Promise<AuthResultDto> {
  const { data } = await authHttp.post<AuthResultDto>('/api/auth/login', request);
  return persist(data);
}

export async function register(request: RegisterRequest): Promise<AuthResultDto> {
  const { data } = await authHttp.post<AuthResultDto>('/api/auth/register', request);
  return persist(data);
}

export async function refresh(): Promise<AuthResultDto> {
  const session = await loadSession();
  if (!session) {
    throw new Error('No stored session to refresh.');
  }
  const { data } = await authHttp.post<AuthResultDto>('/api/auth/refresh', {
    userId: session.userId,
    refreshToken: session.refreshToken,
  });
  return persist(data);
}

export async function logout(): Promise<void> {
  await clearSession();
  setAccessToken(null);
}

// Call once at app startup: restores a previous session optimistically (an expired access
// token still triggers the normal 401 -> refresh flow on the first authenticated request).
export async function restoreSession(): Promise<boolean> {
  const session = await loadSession();
  if (!session) {
    return false;
  }
  setAccessToken(session.accessToken);
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
