import { useCallback, useState } from 'react';
import { useFocusEffect } from '@react-navigation/native';
import { restoreSession } from '../services/authService';
import { getAllTasks } from '../services/taskService';
import type { ApiError } from '../types/api';
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
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  // When the data on screen was last fetched from the server (shown as "Updated 12:30" on the dashboard).
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

  // Stable identity: it is passed down to memoized task cards.
  const reload = useCallback(() => fetchAll(true), [fetchAll]);

  return { state, tasks, teams, errorMessage, refreshing, lastUpdated, refresh, reload };
}
