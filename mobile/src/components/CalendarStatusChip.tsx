import { useCallback, useState } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import { getGoogleCalendarStatus, type GoogleCalendarStatus } from '../services/calendarService';
import { colors, radius, spacing, typography } from '../theme/theme';
import { formatAgo } from '../utils/dashboardStats';

type Props = { onPress: () => void };

export function CalendarStatusChip({ onPress }: Props) {
  const [status, setStatus] = useState<GoogleCalendarStatus | null>(null);
  const [failed, setFailed] = useState(false);

  useFocusEffect(
    useCallback(() => {
      getGoogleCalendarStatus()
        .then((next) => {
          setStatus(next);
          setFailed(false);
        })
        .catch(() => setFailed(true));
    }, []),
  );

  const connected = status?.connected === true;
  const label = failed
    ? 'Google Calendar status unavailable'
    : !status
      ? 'Checking Google Calendar…'
      : connected
        ? `Google Calendar synced ${status.lastSyncedAtUtc ? formatAgo(status.lastSyncedAtUtc) : '— not yet'}`
        : 'Google Calendar not connected — tap to connect';

  return (
    <Pressable testID="calendar-status-chip" onPress={onPress} style={styles.chip}>
      <View style={[styles.dot, { backgroundColor: connected ? colors.success : failed ? colors.danger : colors.urgencyHigh }]} />
      <Text style={styles.text} numberOfLines={1}>
        {label}
      </Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  chip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    backgroundColor: colors.surface,
    borderRadius: radius.md,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
  },
  dot: { width: 10, height: 10, borderRadius: 5 },
  text: { ...typography.body, color: colors.text, flexShrink: 1 },
});
