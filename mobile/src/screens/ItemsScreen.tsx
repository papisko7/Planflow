import { useCallback, useMemo, useRef, useState } from 'react';
import { ActivityIndicator, FlatList, Pressable, RefreshControl, StyleSheet, Text, View } from 'react-native';
import { useNavigation, useScrollToTop } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { AccordionSection } from '../components/AccordionSection';
import { CreateTaskModal } from '../components/CreateTaskModal';
import { Fab } from '../components/Fab';
import { TaskCard } from '../components/TaskCard';
import { useAllTasks } from '../hooks/useAllTasks';
import { useTaskActions } from '../hooks/useTaskActions';
import { colors, radius, spacing, typography } from '../theme/theme';
import type { RootStackParamList } from '../navigation/types';
import { TaskType, type TaskDto, type TaskWithTeam } from '../types/task';
import { buildRows, countByTab, type ItemRow, type ItemTab } from '../utils/itemFilter';

export default function ItemsScreen() {
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const { state, tasks, teams, errorMessage, refreshing, refresh, reload } = useAllTasks();
  const taskActions = useTaskActions(reload);
  const listRef = useRef<FlatList<ItemRow<TaskWithTeam>>>(null);
  useScrollToTop(listRef);
  const [creatingTask, setCreatingTask] = useState(false);
  // Tasks and Events are one list on the server (CalendarItem); the tabs are just a filter over it.
  // All shows both, split into a Tasks and an Events section.
  const [tab, setTab] = useState<ItemTab>('all');
  const rows = useMemo(() => buildRows(tasks, tab), [tasks, tab]);
  const counts = useMemo(() => countByTab(tasks), [tasks]);
  const noun = tab === 'all' ? 'item' : tab === 'events' ? 'event' : 'task';

  const onTaskPress = useCallback(
    (task: TaskDto) => navigation.navigate('TaskDetail', { taskId: task.id, teamId: task.teamId }),
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
        <Text style={styles.subtitle}>Log in to see your team's items.</Text>
      </View>
    );
  }

  if (state === 'error') {
    return (
      <View style={styles.centered}>
        <Text style={styles.title}>Couldn't load items</Text>
        <Text style={styles.subtitle}>{errorMessage}</Text>
      </View>
    );
  }

  return (
    <View style={styles.screen}>
      <FlatList
        ref={listRef}
        style={styles.list}
        data={rows}
        keyExtractor={(row) => row.key}
        refreshControl={<RefreshControl refreshing={refreshing} onRefresh={refresh} tintColor={colors.primary} />}
        renderItem={({ item: row }) =>
          row.type === 'header' ? (
            <Text testID={`items-section-${row.key.replace('header-', '')}`} style={styles.sectionHeader}>
              {row.title} · {row.count}
            </Text>
          ) : row.type === 'group' ? (
            // A Google Tasks list: collapsed until opened, so a long import does not push your own tasks off the screen.
            <View style={styles.group}>
              <AccordionSection
                testID={row.key}
                title={row.title}
                summary={`${row.items.length} ${row.items.length === 1 ? 'task' : 'tasks'} · Google Tasks`}
              >
                {row.items.map((task) => (
                  <TaskCard key={task.id} task={task} onPress={onTaskPress} actions={taskActions} />
                ))}
              </AccordionSection>
            </View>
          ) : (
            <TaskCard task={row.item} onPress={onTaskPress} actions={taskActions} />
          )
        }
        ListHeaderComponent={
          <View style={styles.headerRow}>
            <View style={styles.tabs}>
              {(['all', 'tasks', 'events'] as const).map((t) => (
                <Pressable
                  key={t}
                  testID={`items-tab-${t}`}
                  onPress={() => setTab(t)}
                  style={[styles.tab, tab === t ? styles.tabActive : null]}
                >
                  <Text style={tab === t ? styles.tabTextActive : styles.tabText}>
                    {t === 'all' ? 'All' : t === 'tasks' ? 'Tasks' : 'Events'} ({counts[t]})
                  </Text>
                </Pressable>
              ))}
            </View>
            {teams.length > 0 ? (
              <Pressable testID="create-task-button" onPress={() => setCreatingTask(true)} style={styles.smallButton}>
                <Text style={styles.smallButtonText}>+ New {noun}</Text>
              </Pressable>
            ) : null}
          </View>
        }
        ListEmptyComponent={
          <View style={styles.centered}>
            <Text style={styles.title}>No {noun}s yet</Text>
            <Text style={styles.subtitle}>Tap + to create your first {noun}.</Text>
          </View>
        }
        contentContainerStyle={rows.length === 0 ? styles.emptyContainer : styles.listContent}
      />
      {teams.length > 0 ? <Fab onPress={() => setCreatingTask(true)} /> : null}
      <CreateTaskModal
        visible={creatingTask}
        teams={teams}
        tasks={tasks}
        defaultType={tab === 'events' ? TaskType.Event : TaskType.Task}
        onClose={() => setCreatingTask(false)}
        onCreated={reload}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.background },
  list: { flex: 1, backgroundColor: colors.background },
  listContent: { paddingVertical: spacing.md, paddingBottom: 96 },
  emptyContainer: { flexGrow: 1 },
  centered: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: spacing.lg, gap: spacing.xs },
  title: { ...typography.title, color: colors.text },
  subtitle: { ...typography.body, color: colors.textMuted },
  headerRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: spacing.xs,
    paddingHorizontal: spacing.lg,
    marginBottom: spacing.sm,
  },
  tabs: { flexDirection: 'row', gap: spacing.xs },
  tab: {
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.lg,
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xs,
  },
  tabActive: { backgroundColor: colors.primaryMuted, borderColor: colors.primary },
  tabText: { ...typography.body, color: colors.textMuted },
  tabTextActive: { ...typography.body, color: colors.primary, fontWeight: '600' },
  group: { marginHorizontal: spacing.lg, marginTop: spacing.sm },
  sectionHeader: {
    ...typography.body,
    fontWeight: '700',
    color: colors.textMuted,
    textTransform: 'uppercase',
    paddingHorizontal: spacing.lg,
    paddingTop: spacing.md,
    paddingBottom: spacing.xs,
  },
  smallButton: {
    backgroundColor: colors.primary,
    borderRadius: radius.sm,
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xs,
  },
  smallButtonText: { color: colors.surface, fontWeight: '700', fontSize: 12 },
});
