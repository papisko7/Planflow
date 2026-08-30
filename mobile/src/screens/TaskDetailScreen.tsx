import { useCallback, useEffect, useState } from 'react';
import { ActivityIndicator, ScrollView, StyleSheet, Text, View } from 'react-native';
import { useRoute, type RouteProp } from '@react-navigation/native';
import { UrgencyBreakdown } from '../components/UrgencyBreakdown';
import { getTask, getTeamTasks } from '../services/taskService';
import { colors, radius, spacing, typography } from '../theme/theme';
import type { RootStackParamList } from '../navigation/types';
import type { ApiError } from '../types/api';
import type { TaskDetailDto, TaskDto } from '../types/task';
import { TaskStatus } from '../types/task';

type LoadState = 'loading' | 'ready' | 'error';

const STATUS_LABEL: Record<TaskStatus, string> = {
  [TaskStatus.Todo]: 'To Do',
  [TaskStatus.InProgress]: 'In Progress',
  [TaskStatus.Blocked]: 'Blocked',
  [TaskStatus.Done]: 'Done',
  [TaskStatus.Cancelled]: 'Cancelled',
};

function formatDateTime(iso: string | null): string | null {
  if (!iso) return null;
  return new Date(iso).toLocaleString(undefined, { month: 'short', day: 'numeric', year: 'numeric', hour: '2-digit', minute: '2-digit' });
}

export default function TaskDetailScreen() {
  const route = useRoute<RouteProp<RootStackParamList, 'TaskDetail'>>();
  const { taskId } = route.params;

  const [state, setState] = useState<LoadState>('loading');
  const [detail, setDetail] = useState<TaskDetailDto | null>(null);
  // Resolved client-side: the backend only exposes the single upstream `blockedByTaskId` on
  // TaskDto and has no "downstream tasks blocked by this one" endpoint, so both directions of
  // the blocking relationship are derived here from the team's task list.
  const [blockingTask, setBlockingTask] = useState<TaskDto | null>(null);
  const [blockedTasks, setBlockedTasks] = useState<TaskDto[]>([]);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const load = useCallback(async () => {
    setState('loading');
    try {
      const taskDetail = await getTask(taskId);
      setDetail(taskDetail);

      const teamTasks = await getTeamTasks(taskDetail.task.teamId);
      setBlockingTask(teamTasks.find((t) => t.id === taskDetail.task.blockedByTaskId) ?? null);
      setBlockedTasks(teamTasks.filter((t) => t.blockedByTaskId === taskDetail.task.id));

      setState('ready');
    } catch (err) {
      const apiError = err as ApiError;
      setErrorMessage(apiError.message ?? 'Failed to load task.');
      setState('error');
    }
  }, [taskId]);

  useEffect(() => {
    load();
  }, [load]);

  if (state === 'loading') {
    return (
      <View style={styles.centered}>
        <ActivityIndicator size="large" color={colors.primary} />
      </View>
    );
  }

  if (state === 'error' || !detail) {
    return (
      <View style={styles.centered}>
        <Text style={styles.title}>Couldn't load task</Text>
        <Text style={styles.subtitle}>{errorMessage}</Text>
      </View>
    );
  }

  const { task, latestScoreBreakdown } = detail;
  const dueDate = formatDateTime(task.deadlineUtc);
  const createdDate = formatDateTime(task.createdAtUtc);

  return (
    <ScrollView style={styles.container} contentContainerStyle={styles.content}>
      <View style={styles.card}>
        <Text style={styles.taskTitle}>{task.title}</Text>
        <View style={styles.badgeRow}>
          <Text style={styles.statusBadge}>{STATUS_LABEL[task.status]}</Text>
          {task.manualUrgencyOverride !== null ? <Text style={styles.overrideBadge}>Manual override</Text> : null}
        </View>
        {task.description ? <Text style={styles.description}>{task.description}</Text> : null}

        <View style={styles.metaGrid}>
          {createdDate ? <MetaItem label="Created" value={createdDate} /> : null}
          {dueDate ? <MetaItem label="Due" value={dueDate} /> : null}
          <MetaItem label="Impact score" value={task.impactScore.toFixed(2)} />
        </View>
      </View>

      <View style={styles.card}>
        <Text style={styles.sectionTitle}>Blocking Relationships</Text>
        {blockingTask ? (
          <Text style={styles.relationText}>⛔ Blocked by: {blockingTask.title}</Text>
        ) : task.blockedByTaskId ? (
          <Text style={styles.relationText}>⛔ Blocked by a task outside this team's list</Text>
        ) : (
          <Text style={styles.relationTextMuted}>Not blocked by any task.</Text>
        )}
        {blockedTasks.length > 0 ? (
          <View style={styles.relationList}>
            <Text style={styles.relationTextMuted}>Blocks {blockedTasks.length} downstream task(s):</Text>
            {blockedTasks.map((t) => (
              <Text key={t.id} style={styles.relationText}>
                • {t.title}
              </Text>
            ))}
          </View>
        ) : null}
      </View>

      <View style={styles.card}>
        <Text style={styles.sectionTitle}>Urgency Score Breakdown</Text>
        {latestScoreBreakdown ? (
          <UrgencyBreakdown breakdown={latestScoreBreakdown} />
        ) : (
          <Text style={styles.relationTextMuted}>No score has been computed for this task yet.</Text>
        )}
      </View>
    </ScrollView>
  );
}

function MetaItem({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.metaItem}>
      <Text style={styles.metaLabel}>{label}</Text>
      <Text style={styles.metaValue}>{value}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  content: { padding: spacing.lg, gap: spacing.md },
  centered: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: spacing.lg, gap: spacing.xs },
  title: { ...typography.title, color: colors.text },
  subtitle: { ...typography.body, color: colors.textMuted, textAlign: 'center' },
  card: {
    backgroundColor: colors.surface,
    borderRadius: radius.md,
    padding: spacing.md,
    gap: spacing.sm,
  },
  taskTitle: { ...typography.title, color: colors.text },
  badgeRow: { flexDirection: 'row', gap: spacing.sm },
  statusBadge: {
    ...typography.body,
    fontSize: 12,
    fontWeight: '700',
    color: colors.primary,
    backgroundColor: colors.primaryMuted,
    paddingHorizontal: spacing.sm,
    paddingVertical: 2,
    borderRadius: radius.sm,
    overflow: 'hidden',
  },
  overrideBadge: {
    ...typography.body,
    fontSize: 12,
    fontWeight: '700',
    color: colors.textMuted,
    borderWidth: 1,
    borderColor: colors.border,
    paddingHorizontal: spacing.sm,
    paddingVertical: 2,
    borderRadius: radius.sm,
    overflow: 'hidden',
  },
  description: { ...typography.body, color: colors.textMuted },
  metaGrid: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.lg, marginTop: spacing.xs },
  metaItem: { gap: 2 },
  metaLabel: { fontSize: 12, color: colors.textMuted },
  metaValue: { ...typography.body, color: colors.text, fontWeight: '600' },
  sectionTitle: { ...typography.subtitle, color: colors.text },
  relationText: { ...typography.body, color: colors.text },
  relationTextMuted: { ...typography.body, color: colors.textMuted },
  relationList: { gap: 2, marginTop: spacing.xs },
});
