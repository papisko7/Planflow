import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ActivityIndicator, FlatList, Pressable, RefreshControl, StyleSheet, Text, TextInput, View } from 'react-native';
import { useFocusEffect, useNavigation, useScrollToTop } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { restoreSession } from '../services/authService';
import { CreateTaskModal } from '../components/CreateTaskModal';
import { Fab } from '../components/Fab';
import { TaskCard } from '../components/TaskCard';
import { TeamMembersCard } from '../components/TeamMembersCard';
import { TeamSettingsCard } from '../components/TeamSettingsCard';
import { useTaskActions } from '../hooks/useTaskActions';
import { createTeam, getMyTeams, getTeamMembers } from '../services/teamService';
import { getTeamTasks } from '../services/taskService';
import { colors, radius, spacing, typography } from '../theme/theme';
import { isInformational } from '../utils/templateRules';
import { getUrgencyTier, URGENCY_TIER_META } from '../utils/urgencyGrouping';
import type { RootStackParamList } from '../navigation/types';
import type { ApiError } from '../types/api';
import type { TeamDto } from '../types/team';
import type { TaskDto } from '../types/task';

type LoadState = 'loading' | 'ready' | 'error' | 'signedOut';

interface WorkloadRow {
  key: string;
  label: string;
  count: number;
  avgScore: number;
}

function buildWorkload(tasks: TaskDto[], names: Map<string, string>): WorkloadRow[] {
  const buckets = new Map<string, TaskDto[]>();
  for (const task of tasks) {
    if (isInformational(task)) continue; // a day off or a birthday is not workload
    const key = task.assignedUserId ?? 'unassigned';
    if (!buckets.has(key)) buckets.set(key, []);
    buckets.get(key)!.push(task);
  }

  return [...buckets.entries()]
    .map(([key, group]) => ({
      key,
      label: key === 'unassigned' ? 'Unassigned' : (names.get(key) ?? 'Former member'),
      count: group.length,
      avgScore: group.reduce((sum, t) => sum + t.currentUrgencyScore, 0) / group.length,
    }))
    .sort((a, b) => b.count - a.count);
}

export default function TeamsScreen() {
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const [state, setState] = useState<LoadState>('loading');
  const [teams, setTeams] = useState<TeamDto[]>([]);
  const [selectedTeamId, setSelectedTeamId] = useState<string | null>(null);
  const [teamTasks, setTeamTasks] = useState<TaskDto[]>([]);
  const [tasksLoading, setTasksLoading] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [creatingTeam, setCreatingTeam] = useState(false);
  const [creatingTask, setCreatingTask] = useState(false);
  const [newTeamName, setNewTeamName] = useState('');
  const [createSubmitting, setCreateSubmitting] = useState(false);

  const loadTeams = useCallback(async () => {
    const hasSession = await restoreSession();
    if (!hasSession) {
      setState('signedOut');
      return;
    }
    const data = (await getMyTeams()).filter((t) => !t.isPersonal);
    setTeams(data);
    setState('ready');
    setSelectedTeamId((current) => (data.some((t) => t.id === current) ? current : (data[0]?.id ?? null)));
  }, []);

  const load = useCallback(async () => {
    try {
      await loadTeams();
    } catch (err) {
      const apiError = err as ApiError;
      setErrorMessage(apiError.message ?? 'Failed to load teams.');
      setState('error');
    }
  }, [loadTeams]);

  useFocusEffect(
    useCallback(() => {
      load();
    }, [load]),
  );

  const [reloadTick, setReloadTick] = useState(0);
  useFocusEffect(
    useCallback(() => {
      setReloadTick((n) => n + 1);
    }, []),
  );

  const taskActions = useTaskActions(useCallback(() => setReloadTick((n) => n + 1), []));
  const listRef = useRef<FlatList<TeamDto>>(null);
  useScrollToTop(listRef);

  useEffect(() => {
    if (!selectedTeamId) return;
    let cancelled = false;
    setTasksLoading(true);
    getTeamTasks(selectedTeamId)
      .then((data) => {
        if (!cancelled) setTeamTasks(data);
      })
      .catch(() => {
        if (!cancelled) setTeamTasks([]);
      })
      .finally(() => {
        if (!cancelled) setTasksLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [selectedTeamId, reloadTick]);

  const onRefresh = useCallback(async () => {
    setRefreshing(true);
    try {
      await loadTeams();
      if (selectedTeamId) {
        setTeamTasks(await getTeamTasks(selectedTeamId));
      }
    } catch (err) {
      const apiError = err as ApiError;
      setErrorMessage(apiError.message ?? 'Failed to refresh teams.');
      setState('error');
    } finally {
      setRefreshing(false);
    }
  }, [loadTeams, selectedTeamId]);

  const onCreateTeam = useCallback(async () => {
    const name = newTeamName.trim();
    if (!name) return;
    setCreateSubmitting(true);
    try {
      const team = await createTeam({ name, description: null });
      setTeams((current) => [...current, team]);
      setSelectedTeamId(team.id);
      setNewTeamName('');
      setCreatingTeam(false);
    } catch (err) {
      const apiError = err as ApiError;
      setErrorMessage(apiError.message ?? 'Failed to create team.');
    } finally {
      setCreateSubmitting(false);
    }
  }, [newTeamName]);

  const [memberNames, setMemberNames] = useState<Map<string, string>>(new Map());
  useEffect(() => {
    if (!selectedTeamId) return;
    let cancelled = false;
    getTeamMembers(selectedTeamId)
      .then((members) => {
        if (!cancelled) setMemberNames(new Map(members.map((m) => [m.userId, m.userDisplayName])));
      })
      .catch(() => {
        if (!cancelled) setMemberNames(new Map());
      });
    return () => {
      cancelled = true;
    };
  }, [selectedTeamId, reloadTick]);

  const workload = useMemo(() => buildWorkload(teamTasks, memberNames), [teamTasks, memberNames]);
  const selectedTeam = teams.find((t) => t.id === selectedTeamId) ?? null;

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
        <Text style={styles.subtitle}>Log in to see your teams.</Text>
      </View>
    );
  }

  if (state === 'error') {
    return (
      <View style={styles.centered}>
        <Text style={styles.title}>Couldn't load teams</Text>
        <Text style={styles.subtitle}>{errorMessage}</Text>
      </View>
    );
  }

  return (
    <View style={styles.screen}>
    <FlatList
      ref={listRef}
      style={styles.list}
      data={teams}
      keyExtractor={(item) => item.id}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={colors.primary} />}
      ListHeaderComponent={
        <View>
          <View style={styles.headerRow}>
            <Text style={styles.sectionLabel}>Your teams</Text>
            {!creatingTeam ? (
              <Pressable testID="create-team-button" onPress={() => setCreatingTeam(true)} style={styles.smallButton}>
                <Text style={styles.smallButtonText}>+ New team</Text>
              </Pressable>
            ) : null}
          </View>
          {creatingTeam ? (
            <View style={styles.createTeamForm}>
              <TextInput
                testID="create-team-name-input"
                style={styles.input}
                placeholder="Team name"
                placeholderTextColor={colors.textMuted}
                value={newTeamName}
                onChangeText={setNewTeamName}
              />
              <View style={styles.createTeamActions}>
                <Pressable
                  testID="create-team-submit-button"
                  style={styles.smallButton}
                  onPress={onCreateTeam}
                  disabled={createSubmitting}
                >
                  {createSubmitting ? (
                    <ActivityIndicator color={colors.surface} size="small" />
                  ) : (
                    <Text style={styles.smallButtonText}>Create</Text>
                  )}
                </Pressable>
                <Pressable
                  testID="create-team-cancel-button"
                  onPress={() => {
                    setCreatingTeam(false);
                    setNewTeamName('');
                  }}
                >
                  <Text style={styles.subtitle}>Cancel</Text>
                </Pressable>
              </View>
            </View>
          ) : null}
        </View>
      }
      renderItem={({ item }) => {
        const active = item.id === selectedTeamId;
        return (
          <Pressable
            testID={`team-row-${item.id}`}
            onPress={() => setSelectedTeamId(item.id)}
            style={[styles.teamRow, active ? styles.teamRowActive : null]}
          >
            <View style={styles.teamRowText}>
              <Text style={styles.teamName}>{item.name}</Text>
              {item.description ? (
                <Text style={styles.teamDescription} numberOfLines={1}>
                  {item.description}
                </Text>
              ) : null}
            </View>
            <Text style={styles.memberCount}>{item.memberCount} member{item.memberCount === 1 ? '' : 's'}</Text>
          </Pressable>
        );
      }}
      ListFooterComponent={
        selectedTeam ? (
          <>
          <TeamSettingsCard team={selectedTeam} onChanged={load} onDeleted={load} />
          <TeamMembersCard teamId={selectedTeam.id} onChanged={load} />
          <View style={styles.workloadCard}>
            <View style={styles.headerRow}>
              <Text style={styles.sectionLabel}>Items — {selectedTeam.name}</Text>
              <View style={styles.headerButtons}>
              <Pressable testID="team-new-task-button" style={styles.smallButton} onPress={() => setCreatingTask(true)}>
                <Text style={styles.smallButtonText}>+ Item</Text>
              </Pressable>
              <Pressable
                testID="open-team-chat-button"
                style={styles.smallButton}
                onPress={() => navigation.navigate('Chat', { teamId: selectedTeam.id, teamName: selectedTeam.name })}
              >
                <Text style={styles.smallButtonText}>Chat</Text>
              </Pressable>
              </View>
            </View>
            {tasksLoading ? (
              <ActivityIndicator color={colors.primary} style={styles.workloadSpinner} />
            ) : workload.length === 0 ? (
              <Text style={styles.subtitle}>No items yet for this team.</Text>
            ) : (
              workload.map((row) => (
                <View key={row.key} style={styles.workloadRow}>
                  <Text style={styles.workloadLabel}>{row.label}</Text>
                  <View style={styles.workloadMeta}>
                    <Text style={styles.workloadCount}>
                      {row.count} item{row.count === 1 ? '' : 's'}
                    </Text>
                    <Text style={styles.workloadBadge}>
                      {URGENCY_TIER_META[getUrgencyTier(row.avgScore)].badge} avg {row.avgScore.toFixed(2)}
                    </Text>
                  </View>
                </View>
              ))
            )}
            {!tasksLoading
              ? teamTasks.map((task) => (
                  <View key={task.id} style={styles.teamTaskWrap}>
                    <TaskCard
                      task={{ ...task, teamName: undefined }}
                      onPress={() => navigation.navigate('TaskDetail', { taskId: task.id, teamId: task.teamId })}
                      actions={taskActions}
                    />
                  </View>
                ))
              : null}
          </View>
          </>
        ) : null
      }
      ListEmptyComponent={
        <View style={styles.centered}>
          <Text style={styles.title}>No teams yet</Text>
          <Text style={styles.subtitle}>Create or join a team to get started.</Text>
        </View>
      }
      contentContainerStyle={teams.length === 0 ? styles.emptyContainer : styles.listContent}
    />
    {teams.length > 0 ? <Fab onPress={() => setCreatingTask(true)} /> : null}
    <CreateTaskModal
      visible={creatingTask}
      teams={teams}
      tasks={teamTasks}
      defaultTeamId={selectedTeamId}
      onClose={() => setCreatingTask(false)}
      onCreated={() => setReloadTick((n) => n + 1)}
    />
    </View>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.background },
  list: { flex: 1, backgroundColor: colors.background },
  listContent: { padding: spacing.lg, gap: spacing.sm, paddingBottom: 96 },
  headerButtons: { flexDirection: 'row', gap: spacing.sm },
  emptyContainer: { flexGrow: 1 },
  centered: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: spacing.lg, gap: spacing.xs },
  title: { ...typography.title, color: colors.text },
  subtitle: { ...typography.body, color: colors.textMuted, textAlign: 'center' },
  sectionLabel: { ...typography.subtitle, color: colors.text, marginBottom: spacing.sm },
  headerRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  smallButton: {
    backgroundColor: colors.primary,
    borderRadius: radius.sm,
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xs,
  },
  smallButtonText: { color: colors.surface, fontWeight: '700', fontSize: 12 },
  createTeamForm: { gap: spacing.sm, marginBottom: spacing.md },
  createTeamActions: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  input: {
    backgroundColor: colors.surface,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.md,
    padding: spacing.md,
    color: colors.text,
  },
  teamRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    backgroundColor: colors.surface,
    borderRadius: radius.md,
    borderWidth: 1,
    borderColor: colors.border,
    padding: spacing.md,
    marginBottom: spacing.sm,
  },
  teamRowActive: { borderColor: colors.primary, backgroundColor: colors.primaryMuted },
  teamRowText: { flex: 1, gap: 2 },
  teamName: { ...typography.subtitle, color: colors.text },
  teamDescription: { ...typography.body, color: colors.textMuted, fontSize: 12 },
  memberCount: { ...typography.body, color: colors.textMuted, fontSize: 12 },
  workloadCard: {
    backgroundColor: colors.surface,
    borderRadius: radius.md,
    padding: spacing.md,
    marginTop: spacing.md,
    gap: spacing.xs,
  },
  teamTaskWrap: { marginHorizontal: -spacing.lg, marginTop: spacing.xs },
  workloadSpinner: { marginVertical: spacing.md },
  workloadRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingVertical: spacing.xs,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: colors.border,
  },
  workloadLabel: { ...typography.body, color: colors.text, fontWeight: '600' },
  workloadMeta: { flexDirection: 'row', gap: spacing.sm },
  workloadCount: { ...typography.body, color: colors.textMuted, fontSize: 12 },
  workloadBadge: { ...typography.body, color: colors.textMuted, fontSize: 12 },
});
