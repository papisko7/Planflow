import { memo } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { colors, radius, spacing, typography } from '../theme/theme';
import { getUrgencyTier, URGENCY_TIER_META } from '../utils/urgencyGrouping';
import type { TaskDto } from '../types/task';

const TIER_COLOR = {
  critical: colors.urgencyCritical,
  high: colors.urgencyHigh,
  medium: colors.urgencyMedium,
  low: colors.urgencyLow,
} as const;

function formatDeadline(deadlineUtc: string | null): string | null {
  if (!deadlineUtc) return null;
  return new Date(deadlineUtc).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
}

export interface TaskCardProps {
  task: TaskDto;
  onPress?: (task: TaskDto) => void;
}

function TaskCardComponent({ task, onPress }: TaskCardProps) {
  const tier = getUrgencyTier(task.currentUrgencyScore);
  const deadline = formatDeadline(task.deadlineUtc);

  return (
    <Pressable
      testID={`task-card-${task.id}`}
      onPress={onPress ? () => onPress(task) : undefined}
      style={({ pressed }) => [styles.card, { borderLeftColor: TIER_COLOR[tier], opacity: pressed ? 0.7 : 1 }]}
    >
      <View style={styles.headerRow}>
        <Text style={styles.title} numberOfLines={2}>
          {task.title}
        </Text>
        <View style={[styles.scorePill, { backgroundColor: TIER_COLOR[tier] }]}>
          <Text style={styles.scoreText}>{task.currentUrgencyScore.toFixed(2)}</Text>
        </View>
      </View>

      {task.description ? (
        <Text style={styles.description} numberOfLines={2}>
          {task.description}
        </Text>
      ) : null}

      <View style={styles.metaRow}>
        {deadline ? <Text style={styles.metaText}>📅 {deadline}</Text> : null}
        {task.blockedByTaskId ? <Text style={[styles.metaText, styles.blockedText]}>⛔ Blocked</Text> : null}
        {task.manualUrgencyOverride !== null ? <Text style={styles.metaText}>✋ Manual override</Text> : null}
      </View>

      <Text style={styles.tierLabel}>
        {URGENCY_TIER_META[tier].badge} {URGENCY_TIER_META[tier].label}
      </Text>
    </Pressable>
  );
}

// Real-time TaskHub events will patch individual tasks in place (see DashboardScreen); memoizing
// on the task reference keeps re-renders scoped to the one card that actually changed.
export const TaskCard = memo(TaskCardComponent);

const styles = StyleSheet.create({
  card: {
    backgroundColor: colors.surface,
    borderRadius: radius.md,
    borderLeftWidth: 4,
    padding: spacing.md,
    marginHorizontal: spacing.lg,
    marginBottom: spacing.sm,
    gap: spacing.xs,
  },
  headerRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'flex-start', gap: spacing.sm },
  title: { ...typography.subtitle, color: colors.text, flex: 1 },
  scorePill: { borderRadius: radius.sm, paddingHorizontal: spacing.sm, paddingVertical: 2 },
  scoreText: { color: colors.surface, fontSize: 12, fontWeight: '700' },
  description: { ...typography.body, color: colors.textMuted },
  metaRow: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  metaText: { ...typography.body, color: colors.textMuted, fontSize: 12 },
  blockedText: { color: colors.danger },
  tierLabel: { fontSize: 12, color: colors.textMuted, marginTop: 2 },
});
