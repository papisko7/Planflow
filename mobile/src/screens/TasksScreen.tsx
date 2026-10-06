import { useCallback, useRef, useState } from 'react';
import { ActivityIndicator, FlatList, Pressable, RefreshControl, StyleSheet, Text, View } from 'react-native';
import { useNavigation, useScrollToTop } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { CreateTaskModal } from '../components/CreateTaskModal';
import { Fab } from '../components/Fab';
import { TaskCard } from '../components/TaskCard';
import { useAllTasks } from '../hooks/useAllTasks';
import { useTaskActions } from '../hooks/useTaskActions';
import { colors, radius, spacing, typography } from '../theme/theme';
import type { RootStackParamList } from '../navigation/types';
import type { TaskDto, TaskWithTeam } from '../types/task';

export default function TasksScreen() {
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const { state, tasks, teams, errorMessage, refreshing, refresh, reload } = useAllTasks();
  const taskActions = useTaskActions(reload);
  const listRef = useRef<FlatList<TaskWithTeam>>(null);
  useScrollToTop(listRef);
  const [creatingTask, setCreatingTask] = useState(false);

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
    <View style={styles.screen}>
      <FlatList
        ref={listRef}
        style={styles.list}
        data={tasks}
        keyExtractor={(item) => item.id}
        refreshControl={<RefreshControl refreshing={refreshing} onRefresh={refresh} tintColor={colors.primary} />}
        renderItem={({ item }) => <TaskCard task={item} onPress={onTaskPress} actions={taskActions} />}
        ListHeaderComponent={
          <View style={styles.headerRow}>
            <Text style={styles.sectionLabel}>Tasks</Text>
            {teams.length > 0 ? (
              <Pressable testID="create-task-button" onPress={() => setCreatingTask(true)} style={styles.smallButton}>
                <Text style={styles.smallButtonText}>+ New task</Text>
              </Pressable>
            ) : null}
          </View>
        }
        ListEmptyComponent={
          <View style={styles.centered}>
            <Text style={styles.title}>No tasks yet</Text>
            <Text style={styles.subtitle}>Tap + to create your first task.</Text>
          </View>
        }
        contentContainerStyle={tasks.length === 0 ? styles.emptyContainer : styles.listContent}
      />
      {teams.length > 0 ? <Fab onPress={() => setCreatingTask(true)} /> : null}
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
  subtitle: { ...typography.body, color: colors.textMuted },
  headerRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingHorizontal: spacing.lg,
    marginBottom: spacing.sm,
  },
  sectionLabel: { ...typography.subtitle, color: colors.text },
  smallButton: {
    backgroundColor: colors.primary,
    borderRadius: radius.sm,
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xs,
  },
  smallButtonText: { color: colors.surface, fontWeight: '700', fontSize: 12 },
});
