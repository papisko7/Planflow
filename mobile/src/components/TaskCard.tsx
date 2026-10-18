import { memo, useEffect, useRef, useState } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { getCurrentUserId } from '../services/authService';
import { colors, radius, spacing, typography } from '../theme/theme';
import { formatDuration } from '../utils/duration';
import { DOUBLE_TAP_MS } from '../utils/gestures';
import { formatRecurrence, formatWhen, splitReason, TEMPLATE_LABEL } from '../utils/itemDisplay';
import { colorHex } from '../utils/palette';
import { formatSeriesMore } from '../utils/seriesGrouping';
import { isInformational } from '../utils/templateRules';
import { getUrgencyTier, URGENCY_TIER_META } from '../utils/urgencyGrouping';
import { TaskStatus, TaskType, type TaskDto } from '../types/task';
import { ItemTypeBadge } from './ItemTypeBadge';
import { SwipeableRow } from './SwipeableRow';
import { TaskActionMenu } from './TaskActionMenu';

const TIER_COLOR = {
  critical: colors.urgencyCritical,
  high: colors.urgencyHigh,
  medium: colors.urgencyMedium,
  low: colors.urgencyLow,
} as const;

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
  // Selection mode (step 7.5.11). Given, a double tap ticks the card (that starts the mode). While `selectionMode` is on, a tap ticks or
  // unticks, a long press does too, and the swipes are off. Cards of a list without it open instantly, with no double-tap wait.
  onToggleSelect?: (task: TaskDto & { teamName?: string }) => void;
  selectionMode?: boolean;
  selected?: boolean;
}

function TaskCardComponent({ task, onPress, actions, onToggleSelect, selectionMode = false, selected = false }: TaskCardProps) {
  const [menuVisible, setMenuVisible] = useState(false);
  const tier = getUrgencyTier(task.currentUrgencyScore);
  // A day off, a focus block, a working place or a birthday is not ranked: no score pill, no urgency colour, no "why".
  const ranked = !isInformational(task);
  const isEvent = task.taskType === TaskType.Event;
  const when = formatWhen(task);
  const recurrence = formatRecurrence(task);
  const templateLabel = TEMPLATE_LABEL[task.eventTemplate];
  const reason = splitReason(task.reasonText);
  // A list that shows one entry for a whole repeating series says how many other occurrences it stands for.
  const seriesMore = formatSeriesMore(task.seriesMore);

  // Double tap ticks the card and starts the selection mode (done / not done moved to the long-press menu and the selection bar). To tell
  // it apart from a single tap, the single tap (open details) waits DOUBLE_TAP_MS for a possible second tap; cards of a list
  // without selection open instantly.
  const isDone = task.status === TaskStatus.Completed;
  const isUnassigned = !task.assignedUserId;
  // Only the current assignee can abandon a task (the API enforces the same).
  const isMine = !!task.assignedUserId && task.assignedUserId === getCurrentUserId();
  // Guests of an event / co-contractors of a task: shown as a head count, or as "You" when the viewer is one of them.
  const participantIds = task.participantUserIds ?? [];
  const me = getCurrentUserId();
  const takesPart = !!me && participantIds.includes(me);
  const pendingTap = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(
    () => () => {
      if (pendingTap.current) clearTimeout(pendingTap.current);
    },
    [],
  );

  const handlePress = () => {
    if (selectionMode) {
      onToggleSelect?.(task);
      return;
    }
    if (!onToggleSelect) {
      onPress?.(task);
      return;
    }
    if (pendingTap.current) {
      clearTimeout(pendingTap.current);
      pendingTap.current = null;
      onToggleSelect(task);
      return;
    }
    pendingTap.current = setTimeout(() => {
      pendingTap.current = null;
      onPress?.(task);
    }, DOUBLE_TAP_MS);
  };

  const handleLongPress = () => {
    if (selectionMode) onToggleSelect?.(task);
    else setMenuVisible(true);
  };

  const card = (
    <Pressable
      testID={`task-card-${task.id}`}
      onPress={onPress || actions || onToggleSelect ? handlePress : undefined}
      onLongPress={actions || (selectionMode && onToggleSelect) ? handleLongPress : undefined}
      style={({ pressed }) => [
        styles.card,
        { borderLeftColor: ranked ? TIER_COLOR[tier] : colors.border, opacity: pressed ? 0.7 : 1 },
        selected ? styles.cardSelected : null,
      ]}
    >
      <View style={styles.headerRow}>
        {selectionMode ? (
          <View testID={selected ? `task-ticked-${task.id}` : `task-unticked-${task.id}`}>
            <Ionicons name={selected ? 'checkbox' : 'square-outline'} size={22} color={selected ? colors.primary : colors.textMuted} />
          </View>
        ) : null}
        {colorHex(task.colorKey) ? (
          <View testID={`item-color-${task.id}`} style={[styles.colorDot, { backgroundColor: colorHex(task.colorKey)! }]} />
        ) : null}
        <Text style={styles.title} numberOfLines={2}>
          {task.title}
        </Text>
        {ranked ? (
          <View testID={`score-pill-${task.id}`} style={[styles.scorePill, { backgroundColor: TIER_COLOR[tier] }]}>
            <Text style={styles.scoreText}>{task.currentUrgencyScore.toFixed(2)}</Text>
          </View>
        ) : null}
      </View>

      {task.description ? (
        <Text style={styles.description} numberOfLines={2}>
          {task.description}
        </Text>
      ) : null}

      <View style={styles.metaRow}>
        <ItemTypeBadge testID={`item-type-${task.id}`} type={task.taskType} />
        {templateLabel ? <Text testID={`item-template-${task.id}`} style={styles.metaText}>{templateLabel}</Text> : null}
        {task.teamName ? <Text style={styles.teamChip}>👥 {task.teamName}</Text> : null}
        {!task.assignedUserId ? <Text style={styles.metaText}>👤 Unassigned</Text> : null}
        {participantIds.length > 0 ? (
          <Text testID={`item-people-${task.id}`} style={styles.metaText}>
            🤝 {takesPart ? (participantIds.length === 1 ? 'You' : `You +${participantIds.length - 1}`) : `${participantIds.length}`}
          </Text>
        ) : null}
        {when ? <Text style={styles.metaText}>{isEvent ? '🗓' : '📅'} {when}</Text> : null}
        {task.location ? <Text style={styles.metaText}>📍 {task.location}</Text> : null}
        {!isEvent && task.estimatedEffortHours !== null ? (
          <Text style={styles.metaText}>⏱ {formatDuration(task.estimatedEffortHours)}</Text>
        ) : null}
        {recurrence ? <Text style={styles.metaText}>🔁 {recurrence}</Text> : null}
        {seriesMore ? <Text testID={`series-more-${task.id}`} style={styles.metaText}>🔁 {seriesMore}</Text> : null}
        {task.isDataIncomplete ? <Text testID={`needs-details-${task.id}`} style={[styles.metaText, styles.blockedText]}>⚠ Needs details</Text> : null}
        {task.blockedByTaskId ? <Text style={[styles.metaText, styles.blockedText]}>⛔ Blocked</Text> : null}
        {task.manualUrgencyOverride !== null ? <Text style={styles.metaText}>✋ Manual override</Text> : null}
      </View>

      {/* The backend's one-line explanation ("[High] due in 2d, impact 8/10"); older rows without one fall back to the tier. */}
      {!ranked ? null : reason ? (
        <Text style={styles.reasonText} numberOfLines={3}>
          {reason.label ? <Text style={[styles.reasonLabel, { color: TIER_COLOR[tier] }]}>{reason.label} </Text> : null}
          {reason.rest}
        </Text>
      ) : (
        <Text style={styles.tierLabel}>
          {URGENCY_TIER_META[tier].badge} {URGENCY_TIER_META[tier].label}
        </Text>
      )}
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
        onSwipeRight={selectionMode ? undefined : () => actions.remove(task)}
        onSwipeLeft={selectionMode ? undefined : () => (isUnassigned ? actions.claim(task) : actions.edit(task))}
        leftLabel={isUnassigned ? 'Take over' : 'Edit'}
      >
        {card}
      </SwipeableRow>
      <TaskActionMenu
        visible={menuVisible}
        title={task.title}
        onClose={() => setMenuVisible(false)}
        onOpen={() => onPress?.(task)}
        onSelect={onToggleSelect ? () => onToggleSelect(task) : undefined}
        onClaim={isUnassigned ? () => actions.claim(task) : undefined}
        onAbandon={isMine ? () => actions.abandon(task) : undefined}
        onEdit={() => actions.edit(task)}
        onComplete={isEvent || isDone ? undefined : () => actions.complete(task)}
        onReopen={!isEvent && isDone ? () => actions.reopen(task) : undefined}
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
  cardSelected: { backgroundColor: colors.primaryMuted },
  headerRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'flex-start', gap: spacing.sm },
  title: { ...typography.subtitle, color: colors.text, flex: 1 },
  colorDot: { width: 10, height: 10, borderRadius: 5, marginTop: 6 },
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
  reasonText: { fontSize: 12, color: colors.textMuted, marginTop: 2 },
  reasonLabel: { fontWeight: '700' },
  tierLabel: { fontSize: 12, color: colors.textMuted, marginTop: 2 },
});
