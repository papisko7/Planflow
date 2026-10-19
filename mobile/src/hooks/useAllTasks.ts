import { useCallback, useState } from 'react';
import { useFocusEffect } from '@react-navigation/native';
import { restoreSession } from '../services/authService';
import { getAllTasks } from '../services/taskService';
import type { ApiError } from '../types/api';
import type { ItemListDto } from '../types/itemList';
import type { TeamDto } from '../types/team';
import type { TaskWithTeam } from '../types/task';

export type LoadState = 'loading' | 'ready' | 'error' | 'signedOut';

// Shared loader for the Dashboard / Tasks / Calendar tabs: tasks from ALL of the user's teams,
// each tagged with its team name. Tabs stay mounted, so it reloads on every focus (not only on mount),
// which is what makes changes made on other screens visible without a manual app reload.
export function useAllTasks() {
  const [state, setState] = useState<LoadState>('loading');
  const [tasks, setTasks] = useState<TaskWithTeam[]>([]);
  const [teams, setTeams] = useState<TeamDto[]>([]);
  const [lists, setLists] = useState<ItemListDto[]>([]);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);

  const fetchAll = useCallback(async (force = false) => {
    try {
      if (!(await restoreSession())) {
        setState('signedOut');
        return;
      }
      const result = await getAllTasks(force);
      setTeams(result.teams);
      setTasks(result.tasks);
      setLists(result.lists);
      setErrorMessage(null);
      setLastUpdated(new Date());
      setState('ready');
    } catch (err) {
      setErrorMessage((err as ApiError).message ?? 'Failed to load tasks.');
      setState('error');
    }
  }, []);

  useFocusEffect(
    useCallback(() => {
      fetchAll();
    }, [fetchAll]),
  );

  const refresh = useCallback(async () => {
    setRefreshing(true);
    try {
      await fetchAll(true);
    } finally {
      setRefreshing(false);
    }
  }, [fetchAll]);

  const reload = useCallback(() => fetchAll(true), [fetchAll]);

  return { state, tasks, teams, lists, errorMessage, refreshing, lastUpdated, refresh, reload };
}
