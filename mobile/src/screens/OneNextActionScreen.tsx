import { useCallback, useEffect, useMemo, useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Text, View } from 'react-native';
import { useFocusEffect, useRoute, type RouteProp } from '@react-navigation/native';
import { getTeamTasks, updateTask } from '../services/taskService';
import { colors, radius, spacing, typography } from '../theme/theme';
import { getUrgencyTier, URGENCY_TIER_META } from '../utils/urgencyGrouping';
import { selectOneNextAction } from '../utils/oneNextAction';
import { generateMicroBreakdown, type MicroStep } from '../utils/microBreakdownEngine';
import type { RootStackParamList } from '../navigation/types';
import type { ApiError } from '../types/api';
import { TaskStatus, type TaskDto } from '../types/task';
import { PrimaryButton, SecondaryButton } from '../components/FocusActionButton';

type LoadState = 'loading' | 'ready' | 'error';

// A 24h snooze mirrors the domain's existing manual-override mechanism (see UpdateTaskRequest):
// pinning the score low with an expiry lets the task naturally return to normal ranking afterwards,
// with no new backend concept needed.
const SNOOZE_HOURS = 24;
const SNOOZE_OVERRIDE_SCORE = 0;

function formatTimeRemaining(deadlineUtc: string | null): string {
  if (!deadlineUtc) return 'No deadline set';

  const diffMs = new Date(deadlineUtc).getTime() - Date.now();
  if (diffMs <= 0) return 'Overdue';

  const hours = Math.round(diffMs / (60 * 60 * 1000));
  if (hours < 24) return `${hours}h remaining`;

  const days = Math.round(hours / 24);
  return `${days}d remaining`;
}

export default function OneNextActionScreen() {
  const route = useRoute<RouteProp<RootStackParamList, 'OneNextAction'>>();
  const { teamId } = route.params;

  const [state, setState] = useState<LoadState>('loading');
  const [tasks, setTasks] = useState<TaskDto[]>([]);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [actionPending, setActionPending] = useState(false);

  const load = useCallback(async () => {
    setState('loading');
    try {
      const data = await getTeamTasks(teamId);
      setTasks(data);
      setState('ready');
    } catch (err) {
      const apiError = err as ApiError;
      setErrorMessage(apiError.message ?? 'Failed to load tasks.');
      setState('error');
    }
  }, [teamId]);

  useFocusEffect(
    useCallback(() => {
      load();
    }, [load]),
  );

  const activeTask = useMemo(() => selectOneNextAction(tasks), [tasks]);

  const [breakdownSteps, setBreakdownSteps] = useState<MicroStep[] | null>(null);
  const [completedStepIds, setCompletedStepIds] = useState<Set<string>>(new Set());

  // A fresh active task (completed/deferred/reloaded) means any prior breakdown is stale.
  useEffect(() => {
    setBreakdownSteps(null);
    setCompletedStepIds(new Set());
  }, [activeTask?.id]);

  const applyTaskUpdate = useCallback(
    async (task: TaskDto, patch: Partial<TaskDto>) => {
      setActionPending(true);
      try {
        const updated = await updateTask(task.id, {
          title: task.title,
          description: task.description,
          status: task.status,
          assignedUserId: task.assignedUserId,
          deadlineUtc: task.deadlineUtc,
          impactScore: task.impactScore,
          blockedByTaskId: task.blockedByTaskId,
          manualUrgencyOverride: task.manualUrgencyOverride,
          manualUrgencyOverrideExpiresAtUtc: task.manualUrgencyOverrideExpiresAtUtc,
          ...patch,
        });
        setTasks((prev) => prev.map((t) => (t.id === updated.id ? updated : t)));
      } finally {
        setActionPending(false);
      }
    },
    [],
  );

  const onMarkComplete = useCallback(() => {
    if (!activeTask) return;
    applyTaskUpdate(activeTask, { status: TaskStatus.Done });
  }, [activeTask, applyTaskUpdate]);

  const onDefer = useCallback(() => {
    if (!activeTask) return;
    const expiresAt = new Date(Date.now() + SNOOZE_HOURS * 60 * 60 * 1000).toISOString();
    applyTaskUpdate(activeTask, {
      manualUrgencyOverride: SNOOZE_OVERRIDE_SCORE,
      manualUrgencyOverrideExpiresAtUtc: expiresAt,
    });
  }, [activeTask, applyTaskUpdate]);

  // "I'm Stuck": toggle a static, rule-based 5-minute breakdown inline — no navigation away
  // from focus mode, and no AI call (see microBreakdownEngine's template catalog).
  const onBreakDown = useCallback(() => {
    if (!activeTask) return;
    setBreakdownSteps((prev) => (prev ? null : generateMicroBreakdown(activeTask)));
  }, [activeTask]);

  const onToggleStep = useCallback((stepId: string) => {
    setCompletedStepIds((prev) => {
      const next = new Set(prev);
      if (next.has(stepId)) {
        next.delete(stepId);
      } else {
        next.add(stepId);
      }
      return next;
    });
  }, []);

  if (state === 'loading') {
    return (
      <View style={styles.centered}>
        <ActivityIndicator size="large" color={colors.primary} />
      </View>
    );
  }

  if (state === 'error') {
    return (
      <View style={styles.centered}>
        <Text style={styles.title}>Couldn't load Focus Mode</Text>
        <Text style={styles.subtitle}>{errorMessage}</Text>
      </View>
    );
  }

  if (!activeTask) {
    return (
      <View style={styles.centered} testID="one-next-action-empty">
        <Text style={styles.title}>You're all caught up!</Text>
        <Text style={styles.subtitle}>No pending tasks need your attention right now.</Text>
      </View>
    );
  }

  const tier = getUrgencyTier(activeTask.currentUrgencyScore);

  return (
    <View style={styles.container} testID="one-next-action-view">
      <View style={styles.card}>
        <View style={styles.badgeRow}>
          <Text style={styles.badge}>{URGENCY_TIER_META[tier].badge}</Text>
          <Text style={styles.badgeLabel}>{URGENCY_TIER_META[tier].label} urgency</Text>
        </View>

        <Text style={styles.taskTitle}>{activeTask.title}</Text>
        {activeTask.description ? <Text style={styles.description}>{activeTask.description}</Text> : null}

        <Text style={styles.timeRemaining}>⏳ {formatTimeRemaining(activeTask.deadlineUtc)}</Text>
        <Text style={styles.scoreText}>Urgency score: {activeTask.currentUrgencyScore.toFixed(2)}</Text>
      </View>

      <View style={styles.actions}>
        <PrimaryButton testID="focus-complete-button" label="✅ Mark as Complete" onPress={onMarkComplete} disabled={actionPending} />
        <SecondaryButton testID="focus-defer-button" label="⏰ Defer / Snooze 24h" onPress={onDefer} disabled={actionPending} />
        <SecondaryButton
          testID="focus-breakdown-button"
          label={breakdownSteps ? '🧩 Hide Breakdown' : "🧩 I'm Stuck? Break It Down"}
          onPress={onBreakDown}
          disabled={actionPending}
        />
      </View>

      {breakdownSteps ? (
        <View style={styles.breakdownList} testID="micro-breakdown-list">
          {breakdownSteps.map((step) => {
            const done = completedStepIds.has(step.id);
            return (
              <Pressable
                key={step.id}
                testID={`micro-step-${step.id}`}
                onPress={() => onToggleStep(step.id)}
                style={styles.breakdownRow}
              >
                <Text style={styles.breakdownCheckbox}>{done ? '☑' : '⬜'}</Text>
                <Text style={[styles.breakdownText, done ? styles.breakdownTextDone : null]}>{step.text}</Text>
              </Pressable>
            );
          })}
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background, padding: spacing.lg, gap: spacing.lg },
  centered: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: spacing.lg, gap: spacing.xs },
  title: { ...typography.title, color: colors.text, textAlign: 'center' },
  subtitle: { ...typography.body, color: colors.textMuted, textAlign: 'center' },
  card: { backgroundColor: colors.surface, borderRadius: radius.lg, padding: spacing.lg, gap: spacing.sm },
  badgeRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs },
  badge: { fontSize: 22 },
  badgeLabel: { ...typography.subtitle, color: colors.textMuted },
  taskTitle: { ...typography.title, color: colors.text },
  description: { ...typography.body, color: colors.textMuted },
  timeRemaining: { ...typography.body, color: colors.text, fontWeight: '600', marginTop: spacing.sm },
  scoreText: { ...typography.body, color: colors.textMuted },
  actions: { gap: spacing.sm },
  breakdownList: { backgroundColor: colors.surface, borderRadius: radius.lg, padding: spacing.md, gap: spacing.sm },
  breakdownRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  breakdownCheckbox: { fontSize: 18 },
  breakdownText: { ...typography.body, color: colors.text, flexShrink: 1 },
  breakdownTextDone: { color: colors.textMuted, textDecorationLine: 'line-through' },
});
