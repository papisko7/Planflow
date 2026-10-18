import { useCallback, useMemo, useRef, useState } from 'react';
import {
  ActivityIndicator,
  FlatList,
  type GestureResponderEvent,
  Pressable,
  RefreshControl,
  SectionList,
  StyleSheet,
  Text,
  View,
} from 'react-native';
import { useNavigation, useScrollToTop } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { CreateTaskModal } from '../components/CreateTaskModal';
import { Fab } from '../components/Fab';
import { GoogleCalendarBanner } from '../components/GoogleCalendarBanner';
import { MonthGrid } from '../components/MonthGrid';
import { TaskCard } from '../components/TaskCard';
import { useAllTasks } from '../hooks/useAllTasks';
import { useTaskActions } from '../hooks/useTaskActions';
import { colors, radius, spacing, typography } from '../theme/theme';
import { groupTasksByDueDate } from '../utils/calendarGrouping';
import { dayKey, pinchDirection } from '../utils/dateUtils';
import { isInformational } from '../utils/templateRules';
import { getUrgencyTier } from '../utils/urgencyGrouping';
import type { RootStackParamList } from '../navigation/types';
import { type TaskDto, type TaskWithTeam } from '../types/task';
import { isFinishedStatus } from '../utils/taskStatus';

type ViewMode = 'agenda' | 'month';

const TIER_COLOR = {
  critical: colors.urgencyCritical,
  high: colors.urgencyHigh,
  medium: colors.urgencyMedium,
  low: colors.urgencyLow,
} as const;

// Distance between the first two fingers on the screen.
function fingerDistance(e: GestureResponderEvent): number {
  const [a, b] = e.nativeEvent.touches;
  return Math.hypot(a.pageX - b.pageX, a.pageY - b.pageY);
}

export default function CalendarScreen() {
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const { state, tasks, teams, errorMessage, refreshing, refresh, reload } = useAllTasks();
  const [creatingTask, setCreatingTask] = useState(false);
  const [mode, setMode] = useState<ViewMode>('agenda');
  const [month, setMonth] = useState(new Date());
  const [selectedDay, setSelectedDay] = useState(new Date());
  const taskActions = useTaskActions(reload);
  const listRef = useRef<SectionList<TaskWithTeam>>(null);
  useScrollToTop(listRef);

  const { sections, undated } = useMemo(() => groupTasksByDueDate(tasks), [tasks]);
  const listSections = useMemo(
    () => (undated.length > 0 ? [...sections, { dateKey: 'undated', title: 'No due date', data: undated }] : sections),
    [sections, undated],
  );

  // Month view: one dot per day, coloured by the most urgent open task due that day.
  const markers = useMemo(() => {
    const map = new Map<string, string>();
    for (const section of sections) {
      const open = section.data.filter((t) => !isFinishedStatus(t.status));
      if (open.length === 0) {
        map.set(section.dateKey, colors.border);
        continue;
      }
      // Informational items (a day off, a birthday) have no score, so only ranked ones colour the dot by urgency.
      const ranked = open.filter((t) => !isInformational(t));
      const top = ranked.length > 0 ? Math.max(...ranked.map((t) => t.currentUrgencyScore)) : 0;
      map.set(section.dateKey, TIER_COLOR[getUrgencyTier(top)]);
    }
    return map;
  }, [sections]);
  const selectedKey = dayKey(selectedDay);
  const selectedTasks = useMemo(
    () => sections.find((s) => s.dateKey === selectedKey)?.data ?? [],
    [sections, selectedKey],
  );

  // Pinch to zoom: fingers together = zoom out to the month, fingers apart = zoom into the day list.
  // Plain touch events are used (they never steal the touch from the list), and the gesture is judged
  // once, when a finger lifts: by comparing how far apart the two fingers were at the start and the end.
  const pinch = useRef<{ start: number; last: number } | null>(null);
  const onTouchStart = (e: GestureResponderEvent) => {
    if (e.nativeEvent.touches.length === 2) {
      const d = fingerDistance(e);
      pinch.current = { start: d, last: d };
    }
  };
  const onTouchMove = (e: GestureResponderEvent) => {
    if (pinch.current && e.nativeEvent.touches.length === 2) pinch.current.last = fingerDistance(e);
  };
  const onTouchEnd = () => {
    if (!pinch.current) return;
    const direction = pinchDirection(pinch.current.start, pinch.current.last);
    pinch.current = null;
    if (direction === 'in') setMode('month');
    if (direction === 'out') setMode('agenda');
  };

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
        <Text style={styles.subtitle}>Log in to see your team's calendar.</Text>
      </View>
    );
  }

  if (state === 'error') {
    return (
      <View style={styles.centered}>
        <Text style={styles.title}>Couldn't load calendar</Text>
        <Text style={styles.subtitle}>{errorMessage}</Text>
      </View>
    );
  }

  const modeToggle = (
    <View style={styles.toggleRow}>
      {(['agenda', 'month'] as const).map((m) => (
        <Pressable
          key={m}
          testID={`calendar-mode-${m}`}
          onPress={() => setMode(m)}
          style={[styles.toggle, mode === m ? styles.toggleActive : null]}
        >
          <Text style={mode === m ? styles.toggleTextActive : styles.toggleText}>{m === 'agenda' ? 'Agenda' : 'Month'}</Text>
        </Pressable>
      ))}
    </View>
  );

  return (
    <View
      style={styles.screen}
      onTouchStart={onTouchStart}
      onTouchMove={onTouchMove}
      onTouchEnd={onTouchEnd}
      onTouchCancel={onTouchEnd}
    >
      {mode === 'agenda' ? (
        <SectionList
          ref={listRef}
          style={styles.list}
          sections={listSections}
          keyExtractor={(item) => item.id}
          renderItem={({ item }) => <TaskCard task={item} onPress={onTaskPress} actions={taskActions} />}
          renderSectionHeader={({ section }) => (
            <View style={styles.sectionHeader}>
              <Text style={styles.sectionHeaderText}>{section.title}</Text>
            </View>
          )}
          stickySectionHeadersEnabled
          refreshControl={<RefreshControl refreshing={refreshing} onRefresh={refresh} tintColor={colors.primary} />}
          ListHeaderComponent={
            <>
              <GoogleCalendarBanner onSynced={refresh} />
              {modeToggle}
            </>
          }
          ListEmptyComponent={
            <View style={styles.centered}>
              <Text style={styles.title}>Nothing scheduled</Text>
              <Text style={styles.subtitle}>No tasks with deadlines yet.</Text>
              <Pressable testID="calendar-empty-add-task" onPress={() => setCreatingTask(true)} style={styles.emptyButton}>
                <Text style={styles.emptyButtonText}>+ Add an item</Text>
              </Pressable>
            </View>
          }
          contentContainerStyle={listSections.length === 0 ? styles.emptyContainer : styles.listContent}
        />
      ) : (
        <FlatList
          style={styles.list}
          data={selectedTasks}
          keyExtractor={(item) => item.id}
          renderItem={({ item }) => <TaskCard task={item} onPress={onTaskPress} actions={taskActions} />}
          refreshControl={<RefreshControl refreshing={refreshing} onRefresh={refresh} tintColor={colors.primary} />}
          ListHeaderComponent={
            <>
              <GoogleCalendarBanner onSynced={refresh} />
              {modeToggle}
              <View style={styles.monthWrap}>
                <MonthGrid
                  month={month}
                  onMonthChange={setMonth}
                  selectedKey={selectedKey}
                  markers={markers}
                  onSelectDay={setSelectedDay}
                />
              </View>
              <View style={styles.sectionHeader}>
                <Text style={styles.sectionHeaderText}>
                  {selectedDay.toLocaleDateString(undefined, { weekday: 'long', day: 'numeric', month: 'long' })}
                </Text>
              </View>
            </>
          }
          ListEmptyComponent={<Text style={styles.noTasks}>Nothing due this day.</Text>}
          contentContainerStyle={styles.listContent}
        />
      )}
      <Fab onPress={() => setCreatingTask(true)} />
      <CreateTaskModal
        visible={creatingTask}
        teams={teams}
        tasks={tasks}
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
  subtitle: { ...typography.body, color: colors.textMuted, textAlign: 'center' },
  emptyButton: {
    backgroundColor: colors.primary,
    borderRadius: radius.sm,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    marginTop: spacing.sm,
  },
  emptyButtonText: { color: colors.surface, fontWeight: '700' },
  toggleRow: { flexDirection: 'row', gap: spacing.xs, paddingHorizontal: spacing.lg, paddingBottom: spacing.sm },
  toggle: { borderWidth: 1, borderColor: colors.border, borderRadius: radius.lg, paddingHorizontal: spacing.md, paddingVertical: spacing.xs },
  toggleActive: { backgroundColor: colors.primaryMuted, borderColor: colors.primary },
  toggleText: { ...typography.body, color: colors.textMuted },
  toggleTextActive: { ...typography.body, color: colors.primary, fontWeight: '600' },
  monthWrap: { paddingHorizontal: spacing.md, paddingBottom: spacing.sm },
  noTasks: { ...typography.body, color: colors.textMuted, textAlign: 'center', padding: spacing.lg },
  sectionHeader: {
    backgroundColor: colors.background,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.sm,
  },
  sectionHeaderText: { ...typography.subtitle, color: colors.text },
});
