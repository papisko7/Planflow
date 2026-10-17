import { useCallback, useMemo, useRef, useState } from 'react';
import { ActivityIndicator, Pressable, RefreshControl, SectionList, StyleSheet, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useFocusEffect, useNavigation, useScrollToTop } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { AddMenu } from '../components/AddMenu';
import { CalendarStatusChip } from '../components/CalendarStatusChip';
import { CreateTaskModal } from '../components/CreateTaskModal';
import { CreateTeamModal } from '../components/CreateTeamModal';
import { Fab } from '../components/Fab';
import { TaskCard } from '../components/TaskCard';
import { useAllTasks } from '../hooks/useAllTasks';
import { useTaskActions } from '../hooks/useTaskActions';
import { useUnreadCount } from '../hooks/useUnreadCount';
import { getProfile } from '../services/userService';
import { isPastEvent } from '../utils/schedule';
import { colors, radius, spacing, typography } from '../theme/theme';
import { computeDashboardStats } from '../utils/dashboardStats';
import { selectOneNextAction } from '../utils/oneNextAction';
import { groupTasksByUrgency } from '../utils/urgencyGrouping';
import type { RootStackParamList, RootTabParamList } from '../navigation/types';
import type { TaskDto, TaskWithTeam } from '../types/task';
import { TaskStatus } from '../types/task';

type Shortcut = { testID: string; label: string; icon: keyof typeof Ionicons.glyphMap; onPress: () => void };

export default function DashboardScreen() {
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const { state, tasks, teams, errorMessage, refreshing, lastUpdated, refresh, reload } = useAllTasks();
  const taskActions = useTaskActions(reload);
  const listRef = useRef<SectionList<TaskWithTeam>>(null);
  useScrollToTop(listRef); // tapping the active tab jumps back to the top
  const [addMenuVisible, setAddMenuVisible] = useState(false);
  const [creatingTask, setCreatingTask] = useState(false);
  const [creatingTeam, setCreatingTeam] = useState(false);
  const [displayName, setDisplayName] = useState<string | null>(null);
  const [emailUnconfirmed, setEmailUnconfirmed] = useState(false);
  const { count: unreadCount } = useUnreadCount();

  // Re-read on every focus, so the "confirm your e-mail" banner disappears right after the code was entered in Settings.
  useFocusEffect(
    useCallback(() => {
      getProfile()
        .then((profile) => {
          setDisplayName(profile.displayName);
          setEmailUnconfirmed(!profile.emailVerified);
        })
        .catch(() => setDisplayName(null));
    }, []),
  );

  // Finished / cancelled tasks and events that already took place are history, not "what needs attention" — they stay visible on the Tasks tab.
  const openTasks = useMemo(
    () => tasks.filter((t) => t.status !== TaskStatus.Done && t.status !== TaskStatus.Cancelled && !isPastEvent(t)),
    [tasks],
  );
  const stats = useMemo(() => computeDashboardStats(tasks), [tasks]);
  // "Do now": the single most urgent actionable task is promoted above the list and left out of it.
  const nextTask = useMemo(() => selectOneNextAction(openTasks), [openTasks]);
  const sections = useMemo(() => groupTasksByUrgency(openTasks.filter((t) => t.id !== nextTask?.id)), [openTasks, nextTask]);

  const onTaskPress = useCallback(
    (task: TaskDto) => navigation.navigate('TaskDetail', { taskId: task.id, teamId: task.teamId }),
    [navigation],
  );
  const goToTab = useCallback(
    (screen: keyof RootTabParamList) => navigation.navigate('Tabs', { screen }),
    [navigation],
  );

  const shortcuts: Shortcut[] = [
    { testID: 'shortcut-tasks', label: 'Tasks', icon: 'checkbox-outline', onPress: () => goToTab('Tasks') },
    { testID: 'shortcut-calendar', label: 'Calendar', icon: 'calendar-outline', onPress: () => goToTab('Calendar') },
    { testID: 'shortcut-teams', label: 'Teams', icon: 'people-outline', onPress: () => goToTab('Teams') },
  ];

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

  const header = (
    <View style={styles.header}>
      <View style={styles.greetingRow}>
        <View style={styles.greetingText}>
          <Text style={styles.title} numberOfLines={1}>
            {displayName ? `Hi, ${displayName}` : 'Hi there'}
          </Text>
          <Text style={styles.subtitle}>
            {teams.filter((t) => !t.isPersonal).length} team(s)
            {lastUpdated ? ` · updated ${lastUpdated.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}` : ''}
          </Text>
        </View>
        <Pressable testID="shortcut-notifications" onPress={() => navigation.navigate('Notifications')} hitSlop={8} style={styles.bell}>
          <Ionicons name={unreadCount > 0 ? 'notifications' : 'notifications-outline'} size={28} color={colors.primary} />
          {unreadCount > 0 ? (
            <View testID="notifications-badge" style={styles.bellBadge}>
              <Text style={styles.bellBadgeText}>{unreadCount > 99 ? '99+' : unreadCount}</Text>
            </View>
          ) : null}
        </Pressable>
        <Pressable testID="shortcut-profile" onPress={() => goToTab('Settings')} hitSlop={8}>
          <Ionicons name="person-circle-outline" size={36} color={colors.primary} />
        </Pressable>
      </View>

      {emailUnconfirmed ? (
        <Pressable testID="email-unconfirmed-banner" style={styles.emailBanner} onPress={() => goToTab('Settings')}>
          <Ionicons name="mail-unread-outline" size={18} color={colors.primary} />
          <Text style={styles.emailBannerText}>Confirm your e-mail to get notifications — tap to enter the code.</Text>
        </Pressable>
      ) : null}

      <View style={styles.statsRow}>
        <StatCard testID="stat-open" label="Open" value={String(stats.open)} />
        <StatCard
          testID="stat-overdue"
          label="Overdue"
          value={String(stats.overdue)}
          color={stats.overdue > 0 ? colors.danger : colors.success}
        />
        <StatCard testID="stat-today" label="Due today" value={String(stats.dueToday)} />
        <StatCard testID="stat-done" label="Done" value={`${stats.done}/${stats.total}`} color={colors.success} />
      </View>

      <CalendarStatusChip onPress={() => goToTab('Calendar')} />

      <View style={styles.shortcutRow}>
        {shortcuts.map((s) => (
          <Pressable key={s.testID} testID={s.testID} onPress={s.onPress} style={styles.shortcut}>
            <Ionicons name={s.icon} size={24} color={colors.primary} />
            <Text style={styles.shortcutLabel}>{s.label}</Text>
          </Pressable>
        ))}
      </View>

      {nextTask ? (
        <View testID="dashboard-do-now">
          <Text style={styles.sectionHeaderText}>Do now</Text>
        </View>
      ) : null}
    </View>
  );

  return (
    <View style={styles.screen}>
      <SectionList
        ref={listRef}
        style={styles.list}
        sections={sections}
        keyExtractor={(item) => item.id}
        renderItem={({ item }) => <TaskCard task={item} onPress={onTaskPress} actions={taskActions} />}
        renderSectionHeader={({ section }) => (
          <View testID={`dashboard-section-${section.tier}`} style={styles.sectionHeader}>
            <Text style={styles.sectionHeaderText}>{section.title}</Text>
          </View>
        )}
        ListHeaderComponent={
          <>
            {header}
            {nextTask ? <TaskCard task={nextTask} onPress={onTaskPress} actions={taskActions} /> : null}
          </>
        }
        stickySectionHeadersEnabled
        refreshControl={<RefreshControl refreshing={refreshing} onRefresh={refresh} tintColor={colors.primary} />}
        ListEmptyComponent={
          nextTask ? null : (
            <View style={styles.empty}>
              <Text style={styles.title}>All clear</Text>
              <Text style={styles.subtitle}>No open tasks right now.</Text>
              <Pressable testID="dashboard-empty-add-task" onPress={() => setCreatingTask(true)} style={styles.emptyButton}>
                <Text style={styles.emptyButtonText}>+ Add a task</Text>
              </Pressable>
            </View>
          )
        }
        contentContainerStyle={styles.listContent}
      />
      <Fab onPress={() => setAddMenuVisible(true)} accessibilityLabel="Add task or team" />
      <AddMenu
        visible={addMenuVisible}
        onClose={() => setAddMenuVisible(false)}
        onNewTask={() => setCreatingTask(true)}
        onNewTeam={() => setCreatingTeam(true)}
      />
      <CreateTaskModal
        visible={creatingTask}
        teams={teams}
        tasks={tasks}
        onClose={() => setCreatingTask(false)}
        onCreated={reload}
      />
      <CreateTeamModal visible={creatingTeam} onClose={() => setCreatingTeam(false)} onCreated={reload} />
    </View>
  );
}

function StatCard({ testID, label, value, color = colors.text }: { testID: string; label: string; value: string; color?: string }) {
  return (
    <View testID={testID} style={styles.statCard}>
      <Text style={[styles.statValue, { color }]}>{value}</Text>
      <Text style={styles.statLabel}>{label}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.background },
  list: { flex: 1, backgroundColor: colors.background },
  listContent: { paddingBottom: 96 },
  centered: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: spacing.lg, gap: spacing.xs },
  empty: { alignItems: 'center', padding: spacing.lg, gap: spacing.xs },
  title: { ...typography.title, color: colors.text },
  subtitle: { ...typography.body, color: colors.textMuted, textAlign: 'center' },
  header: { padding: spacing.lg, gap: spacing.md },
  bell: { padding: 2 },
  bellBadge: {
    position: 'absolute',
    top: -4,
    right: -6,
    minWidth: 18,
    height: 18,
    borderRadius: 9,
    paddingHorizontal: 4,
    backgroundColor: colors.danger,
    alignItems: 'center',
    justifyContent: 'center',
  },
  bellBadgeText: { color: colors.surface, fontSize: 11, fontWeight: '700' },
  greetingRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: spacing.md },
  greetingText: { flex: 1, gap: 2 },
  statsRow: { flexDirection: 'row', gap: spacing.sm },
  emailBanner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    backgroundColor: colors.surface,
    borderWidth: 1,
    borderColor: colors.primary,
    borderRadius: radius.md,
    padding: spacing.sm,
  },
  emailBannerText: { ...typography.body, flex: 1, color: colors.text },
  statCard: {
    flex: 1,
    backgroundColor: colors.surface,
    borderRadius: radius.md,
    paddingVertical: spacing.sm,
    alignItems: 'center',
  },
  statValue: { fontSize: 20, fontWeight: '700' },
  statLabel: { ...typography.body, fontSize: 11, color: colors.textMuted },
  shortcutRow: { flexDirection: 'row', gap: spacing.sm },
  shortcut: {
    flex: 1,
    backgroundColor: colors.surface,
    borderRadius: radius.md,
    paddingVertical: spacing.sm,
    alignItems: 'center',
    gap: 2,
  },
  shortcutLabel: { ...typography.body, fontSize: 12, color: colors.text },
  sectionHeader: {
    backgroundColor: colors.background,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.sm,
  },
  sectionHeaderText: { ...typography.subtitle, color: colors.text },
  emptyButton: {
    backgroundColor: colors.primary,
    borderRadius: radius.sm,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    marginTop: spacing.sm,
  },
  emptyButtonText: { color: colors.surface, fontWeight: '700' },
});
