import { useCallback, useEffect, useState } from 'react';
import {
  ActivityIndicator,
  FlatList,
  Pressable,
  RefreshControl,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import { useNavigation } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { TaskCard } from '../components/TaskCard';
import { restoreSession } from '../services/authService';
import { getMyTeams } from '../services/teamService';
import { createTask, getTeamTasks } from '../services/taskService';
import { colors, radius, spacing, typography } from '../theme/theme';
import type { RootStackParamList } from '../navigation/types';
import type { ApiError } from '../types/api';
import type { TaskDto } from '../types/task';

type LoadState = 'loading' | 'ready' | 'error' | 'signedOut';

export default function TasksScreen() {
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const [state, setState] = useState<LoadState>('loading');
  const [teamId, setTeamId] = useState<string | null>(null);
  const [tasks, setTasks] = useState<TaskDto[]>([]);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);

  const [creatingTask, setCreatingTask] = useState(false);
  const [title, setTitle] = useState('');
  const [deadlineDays, setDeadlineDays] = useState('');
  const [impactScore, setImpactScore] = useState('');
  const [blockedByTaskId, setBlockedByTaskId] = useState<string | null>(null);
  const [createSubmitting, setCreateSubmitting] = useState(false);

  const resolveTeam = useCallback(async (): Promise<string | null> => {
    const hasSession = await restoreSession();
    if (!hasSession) {
      setState('signedOut');
      return null;
    }
    const teams = await getMyTeams();
    if (teams.length === 0) {
      setErrorMessage('No teams yet — create a team on the Teams tab first.');
      setState('error');
      return null;
    }
    return teams[0].id;
  }, []);

  const loadTasks = useCallback(async (activeTeamId: string) => {
    const data = await getTeamTasks(activeTeamId);
    setTasks(data);
    setState('ready');
  }, []);

  const load = useCallback(async () => {
    try {
      const resolvedTeamId = teamId ?? (await resolveTeam());
      if (!resolvedTeamId) return;
      setTeamId(resolvedTeamId);
      await loadTasks(resolvedTeamId);
    } catch (err) {
      const apiError = err as ApiError;
      setErrorMessage(apiError.message ?? 'Failed to load tasks.');
      setState('error');
    }
  }, [teamId, resolveTeam, loadTasks]);

  useEffect(() => {
    setState('loading');
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const onRefresh = useCallback(async () => {
    setRefreshing(true);
    try {
      if (teamId) await loadTasks(teamId);
    } finally {
      setRefreshing(false);
    }
  }, [teamId, loadTasks]);

  const onTaskPress = useCallback(
    (task: TaskDto) => navigation.navigate('TaskDetail', { taskId: task.id }),
    [navigation],
  );

  const onCreateTask = useCallback(async () => {
    if (!teamId || !title.trim()) return;
    setCreateSubmitting(true);
    try {
      const days = Number(deadlineDays);
      const deadlineUtc =
        deadlineDays.trim() && !Number.isNaN(days) ? new Date(Date.now() + days * 86_400_000).toISOString() : null;
      const impact = Number(impactScore);

      await createTask(teamId, {
        title: title.trim(),
        description: null,
        assignedUserId: null,
        deadlineUtc,
        impactScore: impactScore.trim() && !Number.isNaN(impact) ? impact : 0,
        blockedByTaskId,
      });

      setTasks(await getTeamTasks(teamId));
      setTitle('');
      setDeadlineDays('');
      setImpactScore('');
      setBlockedByTaskId(null);
      setCreatingTask(false);
    } catch (err) {
      const apiError = err as ApiError;
      setErrorMessage(apiError.message ?? 'Failed to create task.');
    } finally {
      setCreateSubmitting(false);
    }
  }, [teamId, title, deadlineDays, impactScore, blockedByTaskId]);

  if (state === 'loading') {
    return (
      <View style={styles.centered}>
        <ActivityIndicator size="large" color={colors.primary} />
      </View>
    );
  }

  if (state === 'signedOut') {
    return (
      <View style={styles.centered}>
        <Text style={styles.title}>Sign in required</Text>
        <Text style={styles.subtitle}>Log in to see your team's tasks.</Text>
      </View>
    );
  }

  if (state === 'error') {
    return (
      <View style={styles.centered}>
        <Text style={styles.title}>Couldn't load tasks</Text>
        <Text style={styles.subtitle}>{errorMessage}</Text>
      </View>
    );
  }

  return (
    <FlatList
      style={styles.list}
      data={tasks}
      keyExtractor={(item) => item.id}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={colors.primary} />}
      renderItem={({ item }) => <TaskCard task={item} onPress={onTaskPress} />}
      ListHeaderComponent={
        <View style={styles.header}>
          <View style={styles.headerRow}>
            <Text style={styles.sectionLabel}>Tasks</Text>
            {!creatingTask ? (
              <Pressable testID="create-task-button" onPress={() => setCreatingTask(true)} style={styles.smallButton}>
                <Text style={styles.smallButtonText}>+ New task</Text>
              </Pressable>
            ) : null}
          </View>

          {creatingTask ? (
            <View style={styles.form}>
              <TextInput
                testID="create-task-title-input"
                style={styles.input}
                placeholder="Task title"
                placeholderTextColor={colors.textMuted}
                value={title}
                onChangeText={setTitle}
              />
              <TextInput
                testID="create-task-deadline-days-input"
                style={styles.input}
                placeholder="Deadline (days from now, optional)"
                placeholderTextColor={colors.textMuted}
                keyboardType="numeric"
                value={deadlineDays}
                onChangeText={setDeadlineDays}
              />
              <TextInput
                testID="create-task-impact-input"
                style={styles.input}
                placeholder="Impact score (0-10)"
                placeholderTextColor={colors.textMuted}
                keyboardType="numeric"
                value={impactScore}
                onChangeText={setImpactScore}
              />

              {tasks.length > 0 ? (
                <View>
                  <Text style={styles.subtitle}>Blocked by (optional):</Text>
                  <View style={styles.chipRow}>
                    {tasks.map((t) => (
                      <Pressable
                        key={t.id}
                        testID={`blocked-by-chip-${t.id}`}
                        onPress={() => setBlockedByTaskId((current) => (current === t.id ? null : t.id))}
                        style={[styles.chip, blockedByTaskId === t.id ? styles.chipActive : null]}
                      >
                        <Text style={blockedByTaskId === t.id ? styles.chipTextActive : styles.chipText} numberOfLines={1}>
                          {t.title}
                        </Text>
                      </Pressable>
                    ))}
                  </View>
                </View>
              ) : null}

              <View style={styles.createTeamActions}>
                <Pressable
                  testID="create-task-submit-button"
                  style={styles.smallButton}
                  onPress={onCreateTask}
                  disabled={createSubmitting}
                >
                  {createSubmitting ? (
                    <ActivityIndicator color={colors.surface} size="small" />
                  ) : (
                    <Text style={styles.smallButtonText}>Create</Text>
                  )}
                </Pressable>
                <Pressable
                  testID="create-task-cancel-button"
                  onPress={() => {
                    setCreatingTask(false);
                  }}
                >
                  <Text style={styles.subtitle}>Cancel</Text>
                </Pressable>
              </View>
            </View>
          ) : null}
        </View>
      }
      ListEmptyComponent={
        <View style={styles.centered}>
          <Text style={styles.title}>No tasks yet</Text>
          <Text style={styles.subtitle}>Create a task to get started.</Text>
        </View>
      }
      contentContainerStyle={tasks.length === 0 ? styles.emptyContainer : styles.listContent}
    />
  );
}

const styles = StyleSheet.create({
  list: { flex: 1, backgroundColor: colors.background },
  listContent: { paddingVertical: spacing.md },
  emptyContainer: { flexGrow: 1 },
  centered: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: spacing.lg, gap: spacing.xs },
  title: { ...typography.title, color: colors.text },
  subtitle: { ...typography.body, color: colors.textMuted },
  header: { paddingHorizontal: spacing.lg, paddingBottom: spacing.md },
  headerRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', marginBottom: spacing.sm },
  sectionLabel: { ...typography.subtitle, color: colors.text },
  smallButton: {
    backgroundColor: colors.primary,
    borderRadius: radius.sm,
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xs,
  },
  smallButtonText: { color: colors.surface, fontWeight: '700', fontSize: 12 },
  form: { gap: spacing.sm, marginTop: spacing.sm },
  input: {
    backgroundColor: colors.surface,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.md,
    padding: spacing.md,
    color: colors.text,
  },
  chipRow: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs, marginTop: spacing.xs },
  chip: {
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.lg,
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xs,
    maxWidth: 160,
  },
  chipActive: { backgroundColor: colors.primaryMuted, borderColor: colors.primary },
  chipText: { ...typography.body, fontSize: 12, color: colors.textMuted },
  chipTextActive: { ...typography.body, fontSize: 12, color: colors.primary, fontWeight: '600' },
  createTeamActions: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
});
