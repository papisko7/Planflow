import { StyleSheet, Text } from 'react-native';
import { colors, radius } from '../theme/theme';
import { TaskType } from '../types/task';

// Shown on EVERY item so a row reads at a glance as a Task (one deadline, an effort) or an Event (a time range).
export function ItemTypeBadge({ type, testID }: { type: TaskType; testID?: string }) {
  const isEvent = type === TaskType.Event;
  return (
    <Text testID={testID} style={[styles.badge, isEvent ? styles.event : styles.task]}>
      {isEvent ? '🗓 Event' : '☑ Task'}
    </Text>
  );
}

const styles = StyleSheet.create({
  badge: {
    fontSize: 12,
    fontWeight: '700',
    paddingHorizontal: 8,
    borderRadius: radius.sm,
    overflow: 'hidden',
    borderWidth: 1,
  },
  task: { color: colors.primary, borderColor: colors.primary },
  event: { color: colors.urgencyHigh, borderColor: colors.urgencyHigh },
});
