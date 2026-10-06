import { memo, useEffect, useRef, useState } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { getCurrentUserId } from '../services/authService';
import { colors, radius, spacing, typography } from '../theme/theme';
import { getUrgencyTier, URGENCY_TIER_META } from '../utils/urgencyGrouping';
import { TaskStatus, type TaskDto } from '../types/task';
import { SwipeableRow } from './SwipeableRow';
import { TaskActionMenu } from './TaskActionMenu';

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

// Quick actions (see useTaskActions). When given, the card gets swipe gestures
// (right = delete, left = edit) and a long-press menu.
export interface TaskCardActions {
  edit: (task: TaskDto) => void;
  remove: (task: TaskDto) => void;
  complete: (task: TaskDto) => void;
  reopen: (task: TaskDto) => void;
  claim: (task: TaskDto) => void;
  abandon: (task: TaskDto) => void;
}

export interface TaskCardProps {
  // teamName is shown as a chip so tasks from different teams are distinguishable in mixed lists.
  task: TaskDto & { teamName?: string };
  onPress?: (task: TaskDto & { teamName?: string }) => void;
  actions?: TaskCardActions;
}

// Two taps within this window count as a double tap.
const DOUBLE_TAP_MS = 250;

function TaskCardComponent({ task, onPress, actions }: TaskCardProps) {
  const [menuVisible, setMenuVisible] = useState(false);
  const tier = getUrgencyTier(task.currentUrgencyScore);
  const deadline = formatDeadline(task.deadlineUtc);

  // Double tap toggles done: an open task becomes done, a done task is reopened. To tell it apart from a
  // single tap, the single tap (open details) waits DOUBLE_TAP_MS for a possible second tap; cards
  // without actions open instantly because they have no double-tap action.
  const isDone = task.status === TaskStatus.Done;
  const isUnassigned = !task.assignedUserId;
  // Only the current assignee can abandon a task (the API enforces the same).
  const isMine = !!task.assignedUserId && task.assignedUserId === getCurrentUserId();
  const pendingTap = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(
    () => () => {
      if (pendingTap.current) clearTimeout(pendingTap.current);
    },
    [],
  );

  const handlePress = () => {
    if (!actions) {
      onPress?.(task);
      return;
    }
    if (pendingTap.current) {
      clearTimeout(pendingTap.current);
      pendingTap.current = null;
      if (isDone) actions.reopen(task);
      else actions.complete(task);
      return;
    }
    pendingTap.current = setTimeout(() => {
      pendingTap.current = null;
      onPress?.(task);
    }, DOUBLE_TAP_MS);
  };

  const card = (
    <Pressable
      testID={`task-card-${task.id}`}
      onPress={onPress || actions ? handlePress : undefined}
      onLongPress={actions ? () => setMenuVisible(true) : undefined}
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
        {task.teamName ? <Text style={styles.teamChip}>👥 {task.teamName}</Text> : null}
        {!task.assignedUserId ? <Text style={styles.metaText}>👤 Unassigned</Text> : null}
        {deadline ? <Text style={styles.metaText}>📅 {deadline}</Text> : null}
        {task.blockedByTaskId ? <Text style={[styles.metaText, styles.blockedText]}>⛔ Blocked</Text> : null}
        {task.manualUrgencyOverride !== null ? <Text style={styles.metaText}>✋ Manual override</Text> : null}
      </View>

      <Text style={styles.tierLabel}>
        {URGENCY_TIER_META[tier].badge} {URGENCY_TIER_META[tier].label}
      </Text>
    </Pressable>
  );

  if (!actions) {
    return <View style={styles.item}>{card}</View>;
  }

  return (
    <View style={styles.item}>
      {/* Swipe right = delete. Swipe left follows the task: nobody owns it yet -> "Take over",
          otherwise "Edit". Editing an unassigned task is still one tap away in the long-press menu. */}
      <SwipeableRow
        onSwipeRight={() => actions.remove(task)}
        onSwipeLeft={() => (isUnassigned ? actions.claim(task) : actions.edit(task))}
        leftLabel={isUnassigned ? 'Take over' : 'Edit'}
      >
        {card}
      </SwipeableRow>
      <TaskActionMenu
        visible={menuVisible}
        title={task.title}
        onClose={() => setMenuVisible(false)}
        onOpen={() => onPress?.(task)}
        onClaim={isUnassigned ? () => actions.claim(task) : undefined}
        onAbandon={isMine ? () => actions.abandon(task) : undefined}
        onEdit={() => actions.edit(task)}
        onComplete={isDone ? undefined : () => actions.complete(task)}
        onReopen={isDone ? () => actions.reopen(task) : undefined}
        onDelete={() => actions.remove(task)}
      />
    </View>
  );
}

// Real-time TaskHub events will patch individual tasks in place (see DashboardScreen); memoizing
// on the task reference keeps re-renders scoped to the one card that actually changed.
export const TaskCard = memo(TaskCardComponent);

const styles = StyleSheet.create({
  // Outer spacing lives here (not on the card) so the swipe backgrounds line up with the card edges.
  item: { marginHorizontal: spacing.lg, marginBottom: spacing.sm },
  card: {
    backgroundColor: colors.surface,
    borderRadius: radius.md,
    borderLeftWidth: 4,
    padding: spacing.md,
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
  teamChip: {
    ...typography.body,
    fontSize: 12,
    fontWeight: '700',
    color: colors.primary,
    backgroundColor: colors.primaryMuted,
    paddingHorizontal: spacing.sm,
    borderRadius: radius.sm,
    overflow: 'hidden',
  },
  tierLabel: { fontSize: 12, color: colors.textMuted, marginTop: 2 },
});
