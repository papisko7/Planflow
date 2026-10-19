import { useCallback, useEffect, useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Text, View } from 'react-native';
import * as WebBrowser from 'expo-web-browser';
import {
  disconnectGoogleCalendar,
  getGoogleCalendarStatus,
  getGoogleConnectUrl,
  syncGoogleCalendarNow,
  type GoogleCalendarStatus,
} from '../services/calendarService';
import { colors, radius, spacing, typography } from '../theme/theme';
import type { ApiError } from '../types/api';

type Props = { onSynced: () => void };

// The server syncs in the background after the POST returns, so the banner asks the status once a second until it says "not syncing".
const SYNC_POLL_MS = 1000;
const SYNC_MAX_WAIT_MS = 60000;

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// Top-of-calendar strip: "Connect Google" when not linked, otherwise "Connected as … / Sync now / Disconnect". Google is fetched only when
// "Sync now" is pressed (there is no timer), so "Last sync" is the time of the last press. An account linked before Google Tasks existed
// (or one that declined that permission) is also offered a one-tap reconnect that asks for the Tasks permission.
export function GoogleCalendarBanner({ onSynced }: Props) {
  const [status, setStatus] = useState<GoogleCalendarStatus | null>(null);
  const [busy, setBusy] = useState(false);
  const [syncing, setSyncing] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const loadStatus = useCallback(async () => {
    try {
      setStatus(await getGoogleCalendarStatus());
    } catch (err) {
      setError((err as ApiError).message ?? 'Could not check Google Calendar status.');
    }
  }, []);

  useEffect(() => {
    loadStatus();
  }, [loadStatus]);

  // Waits for the server to finish (it may have been started by an earlier press), then lets the screen reload its items.
  const waitForSync = useCallback(async () => {
    setSyncing(true);
    try {
      const deadline = Date.now() + SYNC_MAX_WAIT_MS;
      let next: GoogleCalendarStatus;
      do {
        await sleep(SYNC_POLL_MS);
        next = await getGoogleCalendarStatus();
        setStatus(next);
      } while (next.syncing && Date.now() < deadline);
      onSynced();
    } finally {
      setSyncing(false);
    }
  }, [onSynced]);

  const syncNow = useCallback(async () => {
    setNotice(null);
    const { started } = await syncGoogleCalendarNow();
    if (!started) {
      setNotice('Synced a moment ago - try again in a minute.');
      return;
    }
    await waitForSync();
  }, [waitForSync]);

  const run = useCallback(async (action: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    try {
      await action();
    } catch (err) {
      setError((err as ApiError).message ?? 'Something went wrong.');
    } finally {
      setBusy(false);
    }
  }, []);

  // Opened while a sync is still going (pressed, left the tab, came back): pick the wait up again instead of showing a dead button.
  const serverSyncing = status?.syncing === true;
  useEffect(() => {
    if (serverSyncing && !syncing) run(waitForSync);
  }, [serverSyncing, syncing, run, waitForSync]);

  const connect = () =>
    run(async () => {
      const url = await getGoogleConnectUrl();
      // Resolves when the user closes the browser (after the backend callback page said "connected").
      await WebBrowser.openBrowserAsync(url);
      const next = await getGoogleCalendarStatus();
      setStatus(next);
      if (next.connected) await syncNow();
    });

  const disconnect = () =>
    run(async () => {
      await disconnectGoogleCalendar();
      await loadStatus();
      onSynced();
    });

  return (
    <View style={styles.banner}>
      {status?.connected ? (
        <>
          <Text style={styles.text}>📅 Google Calendar: {status.email}</Text>
          {status.lastSyncedAtUtc && (
            <Text style={styles.hint}>Last sync: {new Date(status.lastSyncedAtUtc).toLocaleString()}</Text>
          )}
          {status.tasksConnected ? (
            <Text testID="google-tasks-status" style={styles.hint}>
              ✅ Google Tasks{status.lastTasksSyncedAtUtc ? `: last sync ${new Date(status.lastTasksSyncedAtUtc).toLocaleString()}` : ': waiting for the first sync'}
            </Text>
          ) : (
            <View style={styles.reconnect}>
              <Text testID="google-tasks-reconnect-notice" style={styles.hint}>
                Google Tasks isn't connected. Reconnect and allow access to your tasks to see them in PlanFlow.
              </Text>
              <Pressable testID="google-tasks-reconnect" style={styles.button} onPress={connect} disabled={busy}>
                <Text style={styles.buttonText}>Reconnect for Google Tasks</Text>
              </Pressable>
            </View>
          )}
          <View style={styles.row}>
            <Pressable style={styles.button} onPress={() => run(syncNow)} disabled={busy}>
              <Text style={styles.buttonText}>{syncing ? 'Syncing…' : 'Sync now'}</Text>
            </Pressable>
            <Pressable style={styles.buttonGhost} onPress={disconnect} disabled={busy}>
              <Text style={styles.buttonGhostText}>Disconnect</Text>
            </Pressable>
          </View>
        </>
      ) : (
        <>
          <Text style={styles.text}>📅 Google Calendar isn't connected.</Text>
          <Pressable style={styles.button} onPress={connect} disabled={busy || !status}>
            <Text style={styles.buttonText}>Connect Google Calendar</Text>
          </Pressable>
        </>
      )}
      {busy && <ActivityIndicator testID="google-banner-busy" color={colors.primary} />}
      {notice && <Text style={styles.hint}>{notice}</Text>}
      {error && <Text style={styles.error}>{error}</Text>}
    </View>
  );
}

const styles = StyleSheet.create({
  banner: {
    marginHorizontal: spacing.lg,
    marginTop: spacing.md,
    padding: spacing.md,
    borderRadius: radius.md,
    backgroundColor: colors.primaryMuted,
    gap: spacing.sm,
  },
  text: { ...typography.body, color: colors.primary },
  hint: { ...typography.body, fontSize: 12, color: colors.textMuted },
  row: { flexDirection: 'row', gap: spacing.sm },
  reconnect: { gap: spacing.xs },
  button: {
    backgroundColor: colors.primary,
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.md,
    borderRadius: radius.sm,
    alignSelf: 'flex-start',
  },
  buttonText: { ...typography.body, color: colors.surface, fontWeight: '600' },
  buttonGhost: { paddingVertical: spacing.sm, paddingHorizontal: spacing.md },
  buttonGhostText: { ...typography.body, color: colors.danger },
  error: { ...typography.body, fontSize: 12, color: colors.danger },
});
