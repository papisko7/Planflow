import { useEffect } from 'react';
import type { NavigationContainerRefWithCurrent } from '@react-navigation/native';
import { markNotificationRead } from '../services/notificationService';
import { configureNotificationHandler, onPushTapped, registerForPush, type PushData } from '../services/pushService';
import type { RootStackParamList } from '../navigation/types';

type NavRef = NavigationContainerRefWithCurrent<RootStackParamList>;

// The data the server attaches to every push (see PushDeliveryJob): enough to open the right screen.
function openFromPush(navigationRef: NavRef, data: PushData): void {
  const taskId = typeof data?.taskId === 'string' ? data.taskId : null;
  const teamId = typeof data?.teamId === 'string' ? data.teamId : null;
  const notificationId = typeof data?.notificationId === 'string' ? data.notificationId : null;

  if (notificationId) markNotificationRead(notificationId).catch(() => undefined);

  const go = () => {
    if (taskId && teamId) navigationRef.navigate('TaskDetail', { taskId, teamId });
    else navigationRef.navigate('Notifications');
  };
  // Right after a cold start the navigator may not be mounted yet.
  if (navigationRef.isReady()) go();
  else setTimeout(() => navigationRef.isReady() && go(), 600);
}

// While signed in: signs the phone up for pushes and opens the right screen when the user taps one.
export function usePushNotifications(signedIn: boolean, navigationRef: NavRef): void {
  useEffect(() => {
    if (!signedIn) return;

    configureNotificationHandler();
    void registerForPush();

    return onPushTapped((data) => openFromPush(navigationRef, data));
  }, [signedIn, navigationRef]);
}
