import { useEffect, useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Switch, Text, View } from 'react-native';
import { AccordionSection } from './AccordionSection';
import { getNotificationPreferences, updateNotificationPreferences, updateTeamNotificationSettings } from '../services/notificationService';
import { PUSH_STATUS_TEXT, getLastPushStatus, registerForPush, type PushStatus } from '../services/pushService';
import { getMyTeams } from '../services/teamService';
import { colors, radius, spacing, typography } from '../theme/theme';
import { LoginNotificationMode, type NotificationPreferences, type NotificationSwitch } from '../types/notification';
import type { ApiError } from '../types/api';
import type { TeamDto } from '../types/team';

type SwitchRow = { key: NotificationSwitch; label: string; hint?: string };

// "Item" = a task or an event: one switch covers both; the notification itself says which one it is.
// One row per category the server lets a user switch on/off (see PlanFlow NotificationPolicy).
const SWITCH_ROWS: SwitchRow[] = [
  { key: 'taskAssignments', label: 'Item assigned to you or taken from you' },
  { key: 'taskChanges', label: 'Someone edits or deletes your item' },
  { key: 'teamTasks', label: 'New item in your teams', hint: 'Added by an owner or admin, even when it is not yours. Can be noisy.' },
  { key: 'taskUrgency', label: 'Your item becomes urgent', hint: 'Due within 24 h or a jump in urgency. At most once a day per item.' },
  { key: 'teamMembership', label: 'Team changes', hint: 'You are added or removed, your role changes, or the team is deleted.' },
  { key: 'chatDigest', label: 'Chat summary while you are offline', hint: 'One e-mail with the number of missed messages, at most once an hour (e-mail only).' },
  { key: 'calendarIssues', label: 'Google Calendar connection problems' },
];

const LOGIN_OPTIONS: { value: LoginNotificationMode; label: string }[] = [
  { value: LoginNotificationMode.NewDevice, label: 'New device' },
  { value: LoginNotificationMode.EveryLogin, label: 'Every sign-in' },
  { value: LoginNotificationMode.Off, label: 'Off' },
];

export function NotificationSettingsCard() {
  const [preferences, setPreferences] = useState<NotificationPreferences | null>(null);
  const [teams, setTeams] = useState<TeamDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [pushStatus, setPushStatus] = useState<PushStatus | null>(getLastPushStatus());

  // What this phone can actually do with pushes (permission, Expo Go limits, ...). Registering again is harmless.
  useEffect(() => {
    registerForPush().then(setPushStatus);
  }, []);

  useEffect(() => {
    Promise.all([getNotificationPreferences(), getMyTeams()])
      .then(([prefs, myTeams]) => {
        setPreferences(prefs);
        // The personal workspace has no other members and no chat, so there is nothing to mute there.
        setTeams(myTeams.filter((team) => !team.isPersonal));
      })
      .catch((err: ApiError) => setError(err.message ?? 'Failed to load notification settings.'))
      .finally(() => setLoading(false));
  }, []);

  // Optimistic: the switch moves at once, and snaps back with a message if the server refuses.
  const savePreferences = async (next: NotificationPreferences) => {
    const previous = preferences;
    setPreferences(next);
    setError(null);
    try {
      setPreferences(await updateNotificationPreferences(next));
    } catch (err) {
      setPreferences(previous);
      setError((err as ApiError).message ?? 'Could not save the change.');
    }
  };

  const saveTeamMute = async (team: TeamDto, changes: Partial<Pick<TeamDto, 'muteNotifications' | 'muteChat'>>) => {
    const updated = { ...team, ...changes };
    setTeams((current) => current.map((t) => (t.id === team.id ? updated : t)));
    setError(null);
    try {
      await updateTeamNotificationSettings(team.id, { muteNotifications: updated.muteNotifications, muteChat: updated.muteChat });
    } catch (err) {
      setTeams((current) => current.map((t) => (t.id === team.id ? team : t)));
      setError((err as ApiError).message ?? 'Could not save the change.');
    }
  };

  // Shown while collapsed: how many of the switches are on.
  const switches = preferences ? [preferences.pushEnabled, preferences.emailEnabled, ...SWITCH_ROWS.map((row) => preferences[row.key])] : [];
  const summary = preferences ? `${switches.filter(Boolean).length} of ${switches.length} on` : undefined;
  const mutedTeams = teams.filter((team) => team.muteNotifications || team.muteChat).length;

  return (
    <AccordionSection testID="notifications-section" title="Notifications" summary={summary}>

      {loading ? <ActivityIndicator color={colors.primary} /> : null}

      {preferences ? (
        <>
          <View style={styles.row}>
            <View style={styles.rowText}>
              <Text style={styles.rowLabel}>Push notifications on my phone</Text>
              <Text testID="push-status" style={styles.hint}>
                {pushStatus ? PUSH_STATUS_TEXT[pushStatus] : 'Checking this phone...'}
              </Text>
            </View>
            <Switch
              testID="notif-push-switch"
              value={preferences.pushEnabled}
              onValueChange={(value) => savePreferences({ ...preferences, pushEnabled: value })}
            />
          </View>

          <View style={styles.row}>
            <View style={styles.rowText}>
              <Text style={styles.rowLabel}>Send me e-mail notifications</Text>
              <Text style={styles.hint}>Welcome, password reset and password-changed e-mails are always sent.</Text>
            </View>
            <Switch
              testID="notif-master-switch"
              value={preferences.emailEnabled}
              onValueChange={(value) => savePreferences({ ...preferences, emailEnabled: value })}
            />
          </View>

          {preferences.emailEnabled ? (
            <>
              <Text style={styles.groupLabel}>Sign-in alerts</Text>
              <View style={styles.chipRow}>
                {LOGIN_OPTIONS.map((option) => (
                  <Pressable
                    key={option.value}
                    testID={`notif-login-${option.value}`}
                    onPress={() => savePreferences({ ...preferences, loginAlerts: option.value })}
                    style={[styles.chip, preferences.loginAlerts === option.value ? styles.chipActive : null]}
                  >
                    <Text style={preferences.loginAlerts === option.value ? styles.chipTextActive : styles.chipText}>{option.label}</Text>
                  </Pressable>
                ))}
              </View>
            </>
          ) : null}

          {/* The categories decide what reaches the inbox, the phone and the mailbox alike. */}
          <Text style={styles.groupLabel}>Tell me when…</Text>
          {SWITCH_ROWS.map((row) => (
            <View key={row.key} style={styles.row}>
              <View style={styles.rowText}>
                <Text style={styles.rowLabel}>{row.label}</Text>
                {row.hint ? <Text style={styles.hint}>{row.hint}</Text> : null}
              </View>
              <Switch
                testID={`notif-switch-${row.key}`}
                value={preferences[row.key]}
                onValueChange={(value) => savePreferences({ ...preferences, [row.key]: value })}
              />
            </View>
          ))}

          {teams.length > 0 ? (
            <AccordionSection
              nested
              testID="notif-teams-section"
              title="Teams"
              summary={mutedTeams === 0 ? 'None muted' : `${mutedTeams} muted`}
            >
              {teams.map((team) => (
                <View key={team.id} style={styles.teamBlock}>
                  <Text style={styles.rowLabel}>{team.name}</Text>
                  <View style={styles.row}>
                    <Text style={[styles.hint, styles.rowText]}>Mute item notifications</Text>
                    <Switch
                      testID={`notif-team-tasks-${team.id}`}
                      value={team.muteNotifications}
                      onValueChange={(value) => saveTeamMute(team, { muteNotifications: value })}
                    />
                  </View>
                  <View style={styles.row}>
                    <Text style={[styles.hint, styles.rowText]}>Mute chat summaries</Text>
                    <Switch
                      testID={`notif-team-chat-${team.id}`}
                      value={team.muteChat}
                      onValueChange={(value) => saveTeamMute(team, { muteChat: value })}
                    />
                  </View>
                </View>
              ))}
            </AccordionSection>
          ) : null}
        </>
      ) : null}

      {error ? <Text style={styles.error}>{error}</Text> : null}
    </AccordionSection>
  );
}

const styles = StyleSheet.create({
  groupLabel: { ...typography.body, fontSize: 12, color: colors.textMuted, marginTop: spacing.sm, textTransform: 'uppercase' },
  row: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: spacing.sm, paddingVertical: spacing.xs },
  rowText: { flex: 1 },
  rowLabel: { ...typography.body, color: colors.text, fontWeight: '600' },
  hint: { ...typography.body, fontSize: 12, color: colors.textMuted },
  teamBlock: { borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: colors.border, paddingTop: spacing.xs },
  chipRow: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs },
  chip: { borderWidth: 1, borderColor: colors.border, borderRadius: radius.lg, paddingHorizontal: spacing.sm, paddingVertical: spacing.xs },
  chipActive: { backgroundColor: colors.primaryMuted, borderColor: colors.primary },
  chipText: { ...typography.body, fontSize: 12, color: colors.textMuted },
  chipTextActive: { ...typography.body, fontSize: 12, color: colors.primary, fontWeight: '600' },
  error: { ...typography.body, color: colors.danger },
});
