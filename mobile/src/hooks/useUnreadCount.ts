import { useCallback, useEffect, useState } from 'react';
import { AppState } from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import { getUnreadCount } from '../services/notificationService';
import { onPushReceived, setAppBadge } from '../services/pushService';

const POLL_MS = 30_000;

export function useUnreadCount(): { count: number; refresh: () => void } {
  const [count, setCount] = useState(0);

  const refresh = useCallback(() => {
    getUnreadCount()
      .then(setCount)
      .catch(() => undefined); // a failed refresh keeps the last known number
  }, []);

  useFocusEffect(
    useCallback(() => {
      refresh();
      const timer = setInterval(refresh, POLL_MS);
      const appState = AppState.addEventListener('change', (state) => state === 'active' && refresh());
      const stopListening = onPushReceived(refresh);
      return () => {
        clearInterval(timer);
        appState.remove();
        stopListening();
      };
    }, [refresh]),
  );

  useEffect(() => {
    void setAppBadge(count);
  }, [count]);

  return { count, refresh };
}
