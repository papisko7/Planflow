import { Platform } from 'react-native';
import Constants, { ExecutionEnvironment } from 'expo-constants';
import * as Device from 'expo-device';
import type * as NotificationsModule from 'expo-notifications';
import { registerPushToken, unregisterPushToken } from './notificationService';

export type PushStatus =
  | 'registered' // this phone is signed up and the server can push to it
  | 'denied' // the user refused the system permission
  | 'simulator' // the iOS simulator has no push support
  | 'expoGoAndroid' // Expo Go on Android cannot receive remote pushes (SDK 53+), a development build can
  | 'noProjectId' // the app has no Expo project id (run `npx eas-cli init` once)
  | 'error'; // network / Expo service problem; trying again later may work

export const PUSH_STATUS_TEXT: Record<PushStatus, string> = {
  registered: 'This phone receives push notifications.',
  denied: 'Push is blocked for PlanFlow. Allow notifications in the phone settings.',
  simulator: 'The iOS simulator cannot receive push notifications. Use a real phone.',
  expoGoAndroid: 'Expo Go on Android cannot receive push notifications. Use a development build or an iPhone.',
  noProjectId: 'Push needs an Expo project id. Run "npx eas-cli init" in the mobile folder once.',
  error: 'Could not reach the push service. Check the internet connection and try again.',
};

function isExpoGoAndroid(): boolean {
  return Platform.OS === 'android' && Constants.executionEnvironment === ExecutionEnvironment.StoreClient;
}

// expo-notifications is loaded on first use, never at import time: in Expo Go on Android merely loading it raises a
// red-screen error (remote push was removed there in SDK 53). On that one setup it is not loaded at all and push is off.
function notifications(): typeof NotificationsModule | null {
  if (isExpoGoAndroid()) return null;
  return require('expo-notifications');
}

let registeredToken: string | null = null;
let lastStatus: PushStatus | null = null;

export function configureNotificationHandler(): void {
  notifications()?.setNotificationHandler({
    handleNotification: async () => ({
      shouldShowBanner: true,
      shouldShowList: true,
      shouldPlaySound: true,
      shouldSetBadge: true,
    }),
  });
}

export function getLastPushStatus(): PushStatus | null {
  return lastStatus;
}

async function ensurePermission(Notifications: typeof NotificationsModule): Promise<boolean> {
  const current = await Notifications.getPermissionsAsync();
  if (current.granted) return true;
  if (!current.canAskAgain) return false;
  return (await Notifications.requestPermissionsAsync()).granted;
}

// Asks for permission, gets this phone's Expo push token and tells the server about it. Safe to call on every start.
// Never throws: the outcome is returned as a status, because a phone without push is a normal situation, not an error.
export async function registerForPush(): Promise<PushStatus> {
  lastStatus = await tryRegister();
  return lastStatus;
}

async function tryRegister(): Promise<PushStatus> {
  const Notifications = notifications();
  if (!Notifications) return 'expoGoAndroid';
  if (Platform.OS === 'ios' && !Device.isDevice) return 'simulator';

  const projectId: string | undefined = Constants.expoConfig?.extra?.eas?.projectId ?? Constants.easConfig?.projectId;
  if (!projectId) return 'noProjectId';

  try {
    if (Platform.OS === 'android') {
      // Android 8+ shows notifications through a channel, and Android 13+ asks for permission only once one exists.
      await Notifications.setNotificationChannelAsync('default', {
        name: 'PlanFlow',
        importance: Notifications.AndroidImportance.MAX,
      });
    }
    if (!(await ensurePermission(Notifications))) return 'denied';

    const { data: token } = await Notifications.getExpoPushTokenAsync({ projectId });
    await registerPushToken(token, Platform.OS);
    registeredToken = token;
    return 'registered';
  } catch {
    return 'error';
  }
}

export async function unregisterPush(): Promise<void> {
  const token = registeredToken;
  registeredToken = null;
  lastStatus = null;
  if (!token) return;
  try {
    await unregisterPushToken(token);
  } catch {
    // Offline sign-out: the server drops dead tokens by itself and moves a token to whoever signs in next on this phone.
  }
}

export async function setAppBadge(count: number): Promise<void> {
  try {
    await notifications()?.setBadgeCountAsync(count);
  } catch {
    // ignore
  }
}

export function onPushReceived(callback: () => void): () => void {
  const subscription = notifications()?.addNotificationReceivedListener(callback);
  return () => subscription?.remove();
}

export type PushData = Record<string, unknown> | undefined;

export function onPushTapped(callback: (data: PushData) => void): () => void {
  const Notifications = notifications();
  if (!Notifications) return () => undefined;

  const subscription = Notifications.addNotificationResponseReceivedListener((response) =>
    callback(response.notification.request.content.data),
  );
  Notifications.getLastNotificationResponseAsync()
    .then((response) => response && callback(response.notification.request.content.data))
    .catch(() => undefined);
  return () => subscription.remove();
}
