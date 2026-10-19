import { useCallback, useMemo, useRef, useState } from 'react';
import { ActivityIndicator, Alert, FlatList, Pressable, RefreshControl, StyleSheet, Text, View } from 'react-native';
import { useNavigation, useScrollToTop } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { AccordionSection } from '../components/AccordionSection';
import { CreateTaskModal } from '../components/CreateTaskModal';
import { Fab } from '../components/Fab';
import { ListFormModal } from '../components/ListFormModal';
import { OptionSheet, type SheetOption } from '../components/OptionSheet';
import { SelectionBar } from '../components/SelectionBar';
import { TaskCard } from '../components/TaskCard';
import { useAllTasks } from '../hooks/useAllTasks';
import { useBulkActions } from '../hooks/useBulkActions';
import { useSelectionSet } from '../hooks/useSelectionSet';
import { useTaskActions } from '../hooks/useTaskActions';
import { deleteList } from '../services/listService';
import { colors, radius, spacing, typography } from '../theme/theme';
import type { RootStackParamList } from '../navigation/types';
import { ItemListKind, type ItemListDto } from '../types/itemList';
import type { ApiError } from '../types/api';
import { TaskType, type TaskDto, type TaskWithTeam } from '../types/task';
import { buildRows, countByTab, filterByTab, type ItemRow, type ItemTab } from '../utils/itemFilter';
import { collapseSeries } from '../utils/seriesGrouping';

const NO_LISTS: ItemListDto[] = [];

type ClearScope = 'tasks' | 'events' | 'all';

export default function ItemsScreen() {
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const { state, tasks, teams, lists = NO_LISTS, errorMessage, refreshing, refresh, reload } = useAllTasks();
  const taskActions = useTaskActions(reload);
  const bulk = useBulkActions(reload);
  const listRef = useRef<FlatList<ItemRow<TaskWithTeam>>>(null);
  useScrollToTop(listRef);
  const [creatingTask, setCreatingTask] = useState(false);
  const [creatingList, setCreatingList] = useState(false);
  const [renamingList, setRenamingList] = useState<ItemListDto | null>(null);
  const [listMenu, setListMenu] = useState<ItemListDto | null>(null);
  const [clearMenu, setClearMenu] = useState(false);
  const [tab, setTab] = useState<ItemTab>('all');
  const shown = useMemo(() => collapseSeries(tasks), [tasks]);
  const rows = useMemo(() => buildRows(shown, tab, new Date(), lists), [shown, tab, lists]);
  const counts = useMemo(() => countByTab(shown), [shown]);
  const noun = tab === 'all' ? 'item' : tab === 'events' ? 'event' : 'task';

  const visible = useMemo(() => filterByTab(shown, tab), [shown, tab]);
  const visibleIds = useMemo(() => visible.map((item) => item.id), [visible]);
  const selection = useSelectionSet(visibleIds);
  const selectedEntries = useMemo(() => visible.filter((item) => selection.selected.has(item.id)), [visible, selection.selected]);
  const { toggle, clear: clearSelection } = selection;
  const onToggleSelect = useCallback((task: TaskDto) => toggle(task.id), [toggle]);

  const onTaskPress = useCallback(
    (task: TaskDto) => navigation.navigate('TaskDetail', { taskId: task.id, teamId: task.teamId }),
    [navigation],
  );

  const changeTab = (next: ItemTab) => {
    clearSelection();
    setTab(next);
  };

  // "Clear": every task / every event (all occurrences of a repeating one) of all the teams. Imported items come back at the next sync.
  const eventTotal = tasks.filter((t) => t.taskType === TaskType.Event).length;
  const taskTotal = tasks.length - eventTotal;
  const clearOptions: SheetOption[] = [
    ...(tab !== 'events' ? [{ key: 'tasks', label: `Clear ${tab === 'all' ? 'tasks' : 'all tasks'} (${taskTotal})`, testID: 'clear-tasks', danger: true }] : []),
    ...(tab !== 'tasks' ? [{ key: 'events', label: `Clear ${tab === 'all' ? 'events' : 'all events'} (${eventTotal})`, testID: 'clear-events', danger: true }] : []),
    ...(tab === 'all' ? [{ key: 'all', label: `Clear all items (${tasks.length})`, testID: 'clear-all', danger: true }] : []),
  ];

  const clearItems = (scope: ClearScope) => {
    const doomed = tasks.filter((t) => scope === 'all' || (scope === 'events') === (t.taskType === TaskType.Event));
    const what = scope === 'all' ? 'items' : scope;
    bulk.deleteItems(doomed, {
      title: `Delete all ${doomed.length} ${doomed.length === 1 ? what.replace(/s$/, '') : what}?`,
      note: doomed.some((t) => t.isExternal)
        ? 'Items imported from Google come back at the next sync, as long as they are still in Google.'
        : 'This cannot be undone.',
      confirmLabel: 'Delete all',
      onDone: clearSelection,
    });
  };

  const askDeleteList = (list: ItemListDto) => {
    Alert.alert(`Delete list "${list.name}"?`, 'The items in it are kept; they just leave the list.', [
      { text: 'Cancel', style: 'cancel' },
      {
        text: 'Delete list',
        style: 'destructive',
        onPress: async () => {
          try {
            await deleteList(list.teamId, list.id);
          } catch (err) {
            const apiError = err as ApiError;
            Alert.alert(
              "Couldn't delete the list",
              apiError.status === 403 ? 'Only the person who made the list, or a team owner or admin, can delete it.' : (apiError.message ?? 'Something went wrong.'),
            );
          }
          await reload();
        },
      },
    ]);
  };

  const groupSummary = (row: Extract<ItemRow<TaskWithTeam>, { type: 'group' }>) => {
    if (!row.list) return `${row.items.length} ${row.items.length === 1 ? 'task' : 'tasks'} · Google Tasks`;
    const word = row.list.kind === ItemListKind.MeetingList ? 'event' : 'task';
    const team = teams.length > 1 ? teams.find((t) => t.id === row.list!.teamId) : undefined;
    return `${row.items.length} ${word}${row.items.length === 1 ? '' : 's'}${team ? ` · ${team.isPersonal ? 'Personal' : team.name}` : ''}`;
  };

  const renderCard = (task: TaskWithTeam) => (
    <TaskCard
      key={task.id}
      task={task}
      onPress={onTaskPress}
      actions={taskActions}
      onToggleSelect={onToggleSelect}
      selectionMode={selection.selecting}
      selected={selection.selected.has(task.id)}
    />
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
        extraData={selection.selected}
        keyExtractor={(row) => row.key}
        refreshControl={<RefreshControl refreshing={refreshing} onRefresh={refresh} tintColor={colors.primary} />}
        renderItem={({ item: row }) =>
          row.type === 'header' ? (
            <Text testID={`items-section-${row.key.replace('header-', '')}`} style={styles.sectionHeader}>
              {row.title} · {row.count}
            </Text>
          ) : row.type === 'group' ? (
            <View style={styles.group}>
              <AccordionSection
                testID={row.key}
                title={row.title}
                summary={groupSummary(row)}
                onHeaderLongPress={row.list ? () => setListMenu(row.list!) : undefined}
              >
                {row.items.length === 0 ? <Text style={styles.emptyGroup}>Empty. Tick items and use "Move to list" to fill it.</Text> : null}
                {row.items.map(renderCard)}
              </AccordionSection>
            </View>
          ) : (
            renderCard(row.item)
          )
        }
        ListHeaderComponent={
          <View style={styles.headerRow}>
            <View style={styles.tabs}>
              {(['all', 'tasks', 'events'] as const).map((t) => (
                <Pressable
                  key={t}
                  testID={`items-tab-${t}`}
                  onPress={() => changeTab(t)}
                  style={[styles.tab, tab === t ? styles.tabActive : null]}
                >
                  <Text style={tab === t ? styles.tabTextActive : styles.tabText}>
                    {t === 'all' ? 'All' : t === 'tasks' ? 'Tasks' : 'Events'} ({counts[t]})
                  </Text>
                </Pressable>
              ))}
            </View>
            {teams.length > 0 ? (
              <View style={styles.buttons}>
                <Pressable testID="create-task-button" onPress={() => setCreatingTask(true)} style={styles.smallButton}>
                  <Text style={styles.smallButtonText}>+ New {noun}</Text>
                </Pressable>
                <Pressable testID="create-list-button" onPress={() => setCreatingList(true)} style={styles.smallButton}>
                  <Text style={styles.smallButtonText}>+ New list</Text>
                </Pressable>
                {tasks.length > 0 ? (
                  <Pressable testID="items-clear-button" onPress={() => setClearMenu(true)} style={styles.clearButton}>
                    <Text style={styles.clearButtonText}>Clear</Text>
                  </Pressable>
                ) : null}
              </View>
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
      {selection.selecting ? (
        <SelectionBar
          selected={selectedEntries}
          allItems={tasks}
          lists={lists}
          teams={teams}
          onSelectAll={selection.selectAll}
          onCancel={clearSelection}
          reload={reload}
        />
      ) : teams.length > 0 ? (
        <Fab onPress={() => setCreatingTask(true)} />
      ) : null}
      <CreateTaskModal
        visible={creatingTask}
        teams={teams}
        tasks={tasks}
        defaultType={tab === 'events' ? TaskType.Event : TaskType.Task}
        onClose={() => setCreatingTask(false)}
        onCreated={reload}
      />
      <ListFormModal
        visible={creatingList}
        teams={teams}
        defaultKind={tab === 'events' ? ItemListKind.MeetingList : ItemListKind.TaskList}
        onClose={() => setCreatingList(false)}
        onSaved={() => void reload()}
      />
      <ListFormModal
        visible={renamingList !== null}
        teams={teams}
        list={renamingList}
        onClose={() => setRenamingList(null)}
        onSaved={() => void reload()}
      />
      <OptionSheet
        visible={listMenu !== null}
        title={listMenu?.name ?? ''}
        options={[
          { key: 'rename', label: 'Rename', testID: 'list-menu-rename' },
          { key: 'delete', label: 'Delete list', testID: 'list-menu-delete', danger: true },
        ]}
        onClose={() => setListMenu(null)}
        onSelect={(key) => {
          const list = listMenu;
          if (!list) return;
          if (key === 'rename') setRenamingList(list);
          else askDeleteList(list);
        }}
      />
      <OptionSheet
        visible={clearMenu}
        title="Clear"
        options={clearOptions}
        onClose={() => setClearMenu(false)}
        onSelect={(key) => clearItems(key as ClearScope)}
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
    gap: spacing.xs,
    paddingHorizontal: spacing.lg,
    marginBottom: spacing.sm,
  },
  tabs: { flexDirection: 'row', gap: spacing.xs },
  buttons: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs, alignItems: 'center' },
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
  emptyGroup: { ...typography.body, fontSize: 12, color: colors.textMuted, paddingBottom: spacing.sm },
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
  clearButton: {
    borderWidth: 1,
    borderColor: colors.danger,
    borderRadius: radius.sm,
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xs,
  },
  clearButtonText: { color: colors.danger, fontWeight: '700', fontSize: 12 },
});
