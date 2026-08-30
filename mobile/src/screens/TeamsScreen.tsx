import { useCallback, useEffect, useMemo, useState } from 'react';
import { ActivityIndicator, FlatList, Pressable, RefreshControl, StyleSheet, Text, View } from 'react-native';
import { restoreSession } from '../services/authService';
import { getMyTeams } from '../services/teamService';
import { getTeamTasks } from '../services/taskService';
import { colors, radius, spacing, typography } from '../theme/theme';
import { getUrgencyTier, URGENCY_TIER_META } from '../utils/urgencyGrouping';
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

// The backend has no GET /api/teams/{id}/members endpoint and no user-lookup endpoint, so a
// member's display name can't be resolved from their id — workload is grouped by assignedUserId
// and labeled with a short id fragment instead of a real name. See PROGRESS.md for the gap.
function buildWorkload(tasks: TaskDto[]): WorkloadRow[] {
  const buckets = new Map<string, TaskDto[]>();
  for (const task of tasks) {
    const key = task.assignedUserId ?? 'unassigned';
    if (!buckets.has(key)) buckets.set(key, []);
    buckets.get(key)!.push(task);
  }

  return [...buckets.entries()]
    .map(([key, group]) => ({
      key,
      label: key === 'unassigned' ? 'Unassigned' : `Member ${key.slice(0, 8)}`,
      count: group.length,
      avgScore: group.reduce((sum, t) => sum + t.currentUrgencyScore, 0) / group.length,
    }))
    .sort((a, b) => b.count - a.count);
}

export default function TeamsScreen() {
  const [state, setState] = useState<LoadState>('loading');
  const [teams, setTeams] = useState<TeamDto[]>([]);
  const [selectedTeamId, setSelectedTeamId] = useState<string | null>(null);
  const [teamTasks, setTeamTasks] = useState<TaskDto[]>([]);
  const [tasksLoading, setTasksLoading] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);

  const loadTeams = useCallback(async () => {
    const hasSession = await restoreSession();
    if (!hasSession) {
      setState('signedOut');
      return;
    }
    const data = await getMyTeams();
    setTeams(data);
    setState('ready');
    if (data.length > 0) {
      setSelectedTeamId((current) => current ?? data[0].id);
    }
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

  useEffect(() => {
    setState('loading');
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

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
  }, [selectedTeamId]);

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

  const workload = useMemo(() => buildWorkload(teamTasks), [teamTasks]);
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
    <FlatList
      style={styles.list}
      data={teams}
      keyExtractor={(item) => item.id}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={colors.primary} />}
      ListHeaderComponent={<Text style={styles.sectionLabel}>Your teams</Text>}
      renderItem={({ item }) => {
        const active = item.id === selectedTeamId;
        return (
          <Pressable
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
          <View style={styles.workloadCard}>
            <Text style={styles.sectionLabel}>Task distribution — {selectedTeam.name}</Text>
            {tasksLoading ? (
              <ActivityIndicator color={colors.primary} style={styles.workloadSpinner} />
            ) : workload.length === 0 ? (
              <Text style={styles.subtitle}>No tasks yet for this team.</Text>
            ) : (
              workload.map((row) => (
                <View key={row.key} style={styles.workloadRow}>
                  <Text style={styles.workloadLabel}>{row.label}</Text>
                  <View style={styles.workloadMeta}>
                    <Text style={styles.workloadCount}>
                      {row.count} task{row.count === 1 ? '' : 's'}
                    </Text>
                    <Text style={styles.workloadBadge}>
                      {URGENCY_TIER_META[getUrgencyTier(row.avgScore)].badge} avg {row.avgScore.toFixed(2)}
                    </Text>
                  </View>
                </View>
              ))
            )}
          </View>
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
  );
}

const styles = StyleSheet.create({
  list: { flex: 1, backgroundColor: colors.background },
  listContent: { padding: spacing.lg, gap: spacing.sm },
  emptyContainer: { flexGrow: 1 },
  centered: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: spacing.lg, gap: spacing.xs },
  title: { ...typography.title, color: colors.text },
  subtitle: { ...typography.body, color: colors.textMuted, textAlign: 'center' },
  sectionLabel: { ...typography.subtitle, color: colors.text, marginBottom: spacing.sm },
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
