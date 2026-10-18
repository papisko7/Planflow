import { useCallback, useMemo } from 'react';
import { Alert } from 'react-native';
import { useNavigation } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { getCurrentUserId } from '../services/authService';
import { deleteTask, updateTask } from '../services/taskService';
import { toUpdateRequest } from '../utils/schedule';
import type { RootStackParamList } from '../navigation/types';
import type { ApiError } from '../types/api';
import { TaskStatus, type TaskDto } from '../types/task';

// Quick actions on a task straight from a list (swipe / long-press menu). `reload` refreshes the calling
// screen after a change. The API decides who may do what (Owner/Admin: any task, Member: own tasks),
// so a refused action just shows a friendly message.
export function useTaskActions(reload: () => unknown) {
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();

  const showError = useCallback((title: string, err: unknown) => {
    const apiError = err as ApiError;
    Alert.alert(
      title,
      apiError.status === 403
        ? 'You can only change items assigned to you (take over an unassigned one first), unless you are the team owner or an admin.'
        : (apiError.message ?? 'Something went wrong.'),
    );
  }, []);

  // Opens the detail screen straight in edit mode.
  const edit = useCallback(
    (task: TaskDto) => navigation.navigate('TaskDetail', { taskId: task.id, teamId: task.teamId, edit: true }),
    [navigation],
  );

  const remove = useCallback(
    (task: TaskDto) => {
      Alert.alert(`Delete "${task.title}"?`, undefined, [
        { text: 'Cancel', style: 'cancel' },
        {
          text: 'Delete',
          style: 'destructive',
          onPress: async () => {
            try {
              await deleteTask(task.id, task.teamId);
              await reload();
            } catch (err) {
              showError("Couldn't delete item", err);
            }
          },
        },
      ]);
    },
    [reload, showError],
  );

  // The update endpoint replaces the whole task, so every field is resent and only the changed one differs.
  const saveChange = useCallback(
    async (task: TaskDto, change: { status?: TaskStatus; assignedUserId?: string | null }, errorTitle: string) => {
      try {
        await updateTask(
          task.id,
          task.teamId,
          toUpdateRequest(task, {
            status: change.status ?? task.status,
            // `null` is a real value here (abandon = clear the assignee), so only `undefined` means "unchanged".
            assignedUserId: change.assignedUserId === undefined ? task.assignedUserId : change.assignedUserId,
          }),
        );
        await reload();
      } catch (err) {
        showError(errorTitle, err);
      }
    },
    [reload, showError],
  );

  const complete = useCallback(
    (task: TaskDto) => saveChange(task, { status: TaskStatus.Completed }, "Couldn't update item"),
    [saveChange],
  );
  // Undo for "done": the task goes back to the backlog and the next urgency recalculation picks it up again.
  const reopen = useCallback(
    (task: TaskDto) => saveChange(task, { status: TaskStatus.Backlog }, "Couldn't update item"),
    [saveChange],
  );
  // Claim: a member takes an unassigned task. From then on it is "theirs", so they may edit and delete it.
  const claim = useCallback(
    (task: TaskDto) => {
      const me = getCurrentUserId();
      return me ? saveChange(task, { assignedUserId: me }, "Couldn't take over item") : undefined;
    },
    [saveChange],
  );

  // Abandon: the opposite of claim. The assignee is cleared and the task goes back to the unassigned pool,
  // where anyone in the team can take it over.
  const abandon = useCallback(
    (task: TaskDto) => saveChange(task, { assignedUserId: null }, "Couldn't abandon item"),
    [saveChange],
  );

  return useMemo(
    () => ({ edit, remove, complete, reopen, claim, abandon }),
    [edit, remove, complete, reopen, claim, abandon],
  );
}
