import { useCallback, useEffect, useMemo, useState } from 'react';
import { ActivityIndicator, RefreshControl, SectionList, StyleSheet, Text, View } from 'react-native';
import { useNavigation } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { TaskCard } from '../components/TaskCard';
import { restoreSession } from '../services/authService';
import { getMyTeams } from '../services/teamService';
import { getTeamTasks } from '../services/taskService';
import { colors, spacing, typography } from '../theme/theme';
import { groupTasksByUrgency } from '../utils/urgencyGrouping';
import type { RootStackParamList } from '../navigation/types';
import type { ApiError } from '../types/api';
import type { TaskDto } from '../types/task';

type LoadState = 'loading' | 'ready' | 'error' | 'signedOut';

export default function DashboardScreen() {
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const [state, setState] = useState<LoadState>('loading');
  const [tasks, setTasks] = useState<TaskDto[]>([]);
  const [teamId, setTeamId] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);

  // Resolves the active team once per mount: restores the persisted session (from Step 3.2),
  // then picks the user's first team. A real team switcher is out of scope for this screen.
  const resolveTeam = useCallback(async (): Promise<string | null> => {
    const hasSession = await restoreSession();
    if (!hasSession) {
      setState('signedOut');
      return null;
    }
    const teams = await getMyTeams();
    if (teams.length === 0) {
      setErrorMessage('No teams yet — create or join a team to see tasks.');
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
      setErrorMessage(apiError.message ?? 'Failed to load dashboard.');
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
      if (teamId) {
        await loadTasks(teamId);
      } else {
        await load();
      }
    } finally {
      setRefreshing(false);
    }
  }, [teamId, loadTasks, load]);

  const sections = useMemo(() => groupTasksByUrgency(tasks), [tasks]);

  const onTaskPress = useCallback(
    (task: TaskDto) => navigation.navigate('TaskDetail', { taskId: task.id }),
    [navigation],
  );

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
        <Text style={styles.title}>Couldn't load dashboard</Text>
        <Text style={styles.subtitle}>{errorMessage}</Text>
      </View>
    );
  }

  return (
    <SectionList
      style={styles.list}
      sections={sections}
      keyExtractor={(item) => item.id}
      renderItem={({ item }) => <TaskCard task={item} onPress={onTaskPress} />}
      renderSectionHeader={({ section }) => (
        <View testID={`dashboard-section-${section.tier}`} style={styles.sectionHeader}>
          <Text style={styles.sectionHeaderText}>{section.title}</Text>
        </View>
      )}
      stickySectionHeadersEnabled
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={colors.primary} />}
      ListEmptyComponent={
        <View style={styles.centered}>
          <Text style={styles.title}>All clear</Text>
          <Text style={styles.subtitle}>No tasks for this team yet.</Text>
        </View>
      }
      contentContainerStyle={sections.length === 0 ? styles.emptyContainer : styles.listContent}
    />
  );
}

const styles = StyleSheet.create({
  list: { flex: 1, backgroundColor: colors.background },
  listContent: { paddingVertical: spacing.md },
  emptyContainer: { flexGrow: 1 },
  centered: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: spacing.lg, gap: spacing.xs },
  title: { ...typography.title, color: colors.text },
  subtitle: { ...typography.body, color: colors.textMuted, textAlign: 'center' },
  sectionHeader: {
    backgroundColor: colors.background,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.sm,
  },
  sectionHeaderText: { ...typography.subtitle, color: colors.text },
});
