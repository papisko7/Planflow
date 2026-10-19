import { useCallback, useMemo } from 'react';
import { Alert } from 'react-native';
import { bulkDeleteTasks, bulkMoveToList, bulkSetStatus } from '../services/taskService';
import type { BulkResult } from '../types/itemList';
import type { ApiError } from '../types/api';
import type { TaskDto, TaskStatus } from '../types/task';
import { groupByTeam } from '../utils/selection';

const NOT_YOURS = 'You can only change items assigned to you, unless you are the team owner or an admin.';

// Actions on many items at once (the selection bar and "Clear"). The API works on one team at a time, so the items are sent team by team.
// What the server leaves alone (an item that is not yours, a status the item's type does not have) is reported once at the end; the rest
// of the batch is still done. `reload` refreshes the calling screen afterwards, also when something failed half way.
export function useBulkActions(reload: () => unknown) {
  const run = useCallback(
    async (items: TaskDto[], call: (teamId: string, ids: string[]) => Promise<BulkResult>, failTitle: string): Promise<boolean> => {
      let skipped = 0;
      try {
        for (const [teamId, group] of groupByTeam(items)) {
          const result = await call(teamId, group.map((item) => item.id));
          skipped += result.skippedIds.length;
        }
      } catch (err) {
        Alert.alert(failTitle, (err as ApiError).message ?? 'Something went wrong.');
        await reload();
        return false;
      }
      await reload();
      if (skipped > 0) {
        Alert.alert(skipped === 1 ? '1 item left unchanged' : `${skipped} items left unchanged`, NOT_YOURS);
      }
      return true;
    },
    [reload],
  );

  const deleteItems = useCallback(
    (items: TaskDto[], options: { title: string; note?: string; confirmLabel?: string; onDone?: () => void }) => {
      if (items.length === 0) return;
      Alert.alert(options.title, options.note, [
        { text: 'Cancel', style: 'cancel' },
        {
          text: options.confirmLabel ?? 'Delete',
          style: 'destructive',
          onPress: async () => {
            if (await run(items, bulkDeleteTasks, "Couldn't delete the items")) options.onDone?.();
          },
        },
      ]);
    },
    [run],
  );

  const setStatus = useCallback(
    async (items: TaskDto[], status: TaskStatus, onDone?: () => void) => {
      if (await run(items, (teamId, ids) => bulkSetStatus(teamId, ids, status), "Couldn't change the status")) onDone?.();
    },
    [run],
  );

  const moveToList = useCallback(
    async (items: TaskDto[], listId: string | null, onDone?: () => void) => {
      if (await run(items, (teamId, ids) => bulkMoveToList(teamId, ids, listId), "Couldn't move the items")) onDone?.();
    },
    [run],
  );

  return useMemo(() => ({ deleteItems, setStatus, moveToList }), [deleteItems, setStatus, moveToList]);
}
