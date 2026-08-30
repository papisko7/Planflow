import * as SecureStore from 'expo-secure-store';

// Persists the auth session so the app survives a restart without forcing re-login;
// SecureStore backs onto Keychain/Keystore, so tokens never sit in plain storage.
const ACCESS_TOKEN_KEY = 'planflow.accessToken';
const REFRESH_TOKEN_KEY = 'planflow.refreshToken';
const USER_ID_KEY = 'planflow.userId';

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
  await Promise.all([
    SecureStore.deleteItemAsync(ACCESS_TOKEN_KEY),
    SecureStore.deleteItemAsync(REFRESH_TOKEN_KEY),
    SecureStore.deleteItemAsync(USER_ID_KEY),
  ]);
}
