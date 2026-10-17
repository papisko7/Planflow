import * as SecureStore from 'expo-secure-store';

const USER_ID_KEY = 'planflow.userId';
const PIN_KEY = (userId: string) => `planflow.pin.${userId}`;
const PIN_ENABLED_KEY = (userId: string) => `planflow.pinEnabled.${userId}`;
const PIN_USER_IDS_KEY = 'planflow.pinUserIds';

async function getStoredUserId(): Promise<string | null> {
  return SecureStore.getItemAsync(USER_ID_KEY);
}

async function getPinUserIds(): Promise<string[]> {
  const json = await SecureStore.getItemAsync(PIN_USER_IDS_KEY);
  if (!json) return [];
  try { return JSON.parse(json); } catch { return []; }
}

async function addPinUserId(userId: string): Promise<void> {
  const ids = await getPinUserIds();
  if (!ids.includes(userId)) {
    ids.push(userId);
    await SecureStore.setItemAsync(PIN_USER_IDS_KEY, JSON.stringify(ids));
  }
}

async function removePinUserId(userId: string): Promise<void> {
  const ids = (await getPinUserIds()).filter(id => id !== userId);
  if (ids.length > 0) {
    await SecureStore.setItemAsync(PIN_USER_IDS_KEY, JSON.stringify(ids));
  } else {
    await SecureStore.deleteItemAsync(PIN_USER_IDS_KEY);
  }
}

export async function isPinEnabledForUser(userId: string): Promise<boolean> {
  const flag = await SecureStore.getItemAsync(PIN_ENABLED_KEY(userId));
  return flag === 'true';
}

export async function verifyPinForUser(userId: string, pin: string): Promise<boolean> {
  const stored = await SecureStore.getItemAsync(PIN_KEY(userId));
  return stored !== null && stored === pin;
}

export async function isPinEnabled(): Promise<boolean> {
  const userId = await getStoredUserId();
  if (!userId) return false;
  const flag = await SecureStore.getItemAsync(PIN_ENABLED_KEY(userId));
  return flag === 'true';
}

export async function isAnyPinEnabled(): Promise<boolean> {
  return (await getPinUserIds()).length > 0;
}

export async function setPin(pin: string): Promise<void> {
  const userId = await getStoredUserId();
  if (!userId) throw new Error('No user signed in');
  await SecureStore.setItemAsync(PIN_KEY(userId), pin);
  await SecureStore.setItemAsync(PIN_ENABLED_KEY(userId), 'true');
  await addPinUserId(userId);
}

export async function verifyPin(pin: string): Promise<boolean> {
  const userId = await getStoredUserId();
  if (!userId) return false;
  const stored = await SecureStore.getItemAsync(PIN_KEY(userId));
  return stored !== null && stored === pin;
}

export async function verifyPinAnyUser(pin: string): Promise<string | null> {
  const userIds = await getPinUserIds();
  for (const userId of userIds) {
    const stored = await SecureStore.getItemAsync(PIN_KEY(userId));
    if (stored !== null && stored === pin) return userId;
  }
  return null;
}

export async function clearPin(): Promise<void> {
  const userId = await getStoredUserId();
  if (!userId) return;
  await SecureStore.deleteItemAsync(PIN_KEY(userId));
  await SecureStore.deleteItemAsync(PIN_ENABLED_KEY(userId));
  await removePinUserId(userId);
}

export async function clearPinForUser(userId: string): Promise<void> {
  await SecureStore.deleteItemAsync(PIN_KEY(userId));
  await SecureStore.deleteItemAsync(PIN_ENABLED_KEY(userId));
  await removePinUserId(userId);
}
