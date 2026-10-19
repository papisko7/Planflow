import * as SecureStore from 'expo-secure-store';

const ACCESS_TOKEN_KEY = 'planflow.accessToken';
const REFRESH_TOKEN_KEY = 'planflow.refreshToken';
const USER_ID_KEY = 'planflow.userId';
const DEVICE_ID_KEY = 'planflow.deviceId';

export interface StoredSession {
  accessToken: string;
  refreshToken: string;
  userId: string;
}

export async function saveSession(session: StoredSession): Promise<void> {
  await Promise.all([
    SecureStore.setItemAsync(ACCESS_TOKEN_KEY, session.accessToken),
    SecureStore.setItemAsync(REFRESH_TOKEN_KEY, session.refreshToken),
    SecureStore.setItemAsync(USER_ID_KEY, session.userId),
    SecureStore.setItemAsync(`${REFRESH_TOKEN_KEY}.${session.userId}`, session.refreshToken),
  ]);
}

export async function loadSession(): Promise<StoredSession | null> {
  const [accessToken, refreshToken, userId] = await Promise.all([
    SecureStore.getItemAsync(ACCESS_TOKEN_KEY),
    SecureStore.getItemAsync(REFRESH_TOKEN_KEY),
    SecureStore.getItemAsync(USER_ID_KEY),
  ]);

  if (!accessToken || !refreshToken || !userId) {
    return null;
  }

  return { accessToken, refreshToken, userId };
}

export async function clearSession(): Promise<void> {
  const userId = await SecureStore.getItemAsync(USER_ID_KEY);
  await Promise.all([
    SecureStore.deleteItemAsync(ACCESS_TOKEN_KEY),
    SecureStore.deleteItemAsync(REFRESH_TOKEN_KEY),
    SecureStore.deleteItemAsync(USER_ID_KEY),
    userId ? SecureStore.deleteItemAsync(`${REFRESH_TOKEN_KEY}.${userId}`) : Promise.resolve(),
  ]);
}

// Removes only the access token — keeps refresh token + userId so PIN login can still call
// refresh() after a soft logout. restoreSession() requires all three, so it will return
// false and the user stays on the login screen until they authenticate again.
export async function clearAccessToken(): Promise<void> {
  await SecureStore.deleteItemAsync(ACCESS_TOKEN_KEY);
}

export interface RefreshCredentials {
  refreshToken: string;
  userId: string;
}

export async function loadRefreshCredentials(): Promise<RefreshCredentials | null> {
  const [refreshToken, userId] = await Promise.all([
    SecureStore.getItemAsync(REFRESH_TOKEN_KEY),
    SecureStore.getItemAsync(USER_ID_KEY),
  ]);
  if (!refreshToken || !userId) return null;
  return { refreshToken, userId };
}

export async function loadRefreshCredentialsForUser(userId: string): Promise<RefreshCredentials | null> {
  const refreshToken = await SecureStore.getItemAsync(`${REFRESH_TOKEN_KEY}.${userId}`);
  if (!refreshToken) return null;
  return { refreshToken, userId };
}

export async function clearRefreshTokenForUser(userId: string): Promise<void> {
  await SecureStore.deleteItemAsync(`${REFRESH_TOKEN_KEY}.${userId}`);
}

export async function getDeviceId(): Promise<string> {
  const existing = await SecureStore.getItemAsync(DEVICE_ID_KEY);
  if (existing) {
    return existing;
  }
  const created = `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}${Math.random().toString(36).slice(2, 12)}`;
  await SecureStore.setItemAsync(DEVICE_ID_KEY, created);
  return created;
}
