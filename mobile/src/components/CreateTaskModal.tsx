import { useCallback, useEffect, useState } from 'react';
import {
  ActivityIndicator,
  KeyboardAvoidingView,
  Modal,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import { getCurrentUserId } from '../services/authService';
import { createTask } from '../services/taskService';
import { useTeamAvailability } from '../hooks/useTeamAvailability';
import { getTeamMembers } from '../services/teamService';
import { colors, radius, spacing, typography } from '../theme/theme';
import { AssigneePicker } from './AssigneePicker';
import { ListPicker } from './ListPicker';
import { ParticipantPicker } from './ParticipantPicker';
import { RemindersEditor } from './RemindersEditor';
import { ScheduleForm } from './ScheduleForm';
import type { ApiError } from '../types/api';
import { TeamRole, type TeamDto, type TeamMemberDto } from '../types/team';
import { TaskType, type ReminderInput, type ScheduleFields, type TaskDto } from '../types/task';
import { ItemListKind } from '../types/itemList';
import { collapseSeries } from '../utils/seriesGrouping';
import { isFinishedStatus } from '../utils/taskStatus';
import { emptySchedule, scheduleForRequest, validateSchedule } from '../utils/schedule';
import { fitReminders, validateReminders, type ReminderContext } from '../utils/reminders';
import { isInformational, rulesFor } from '../utils/templateRules';

type Props = {
  visible: boolean;
  teams: TeamDto[];
  // Existing tasks, offered as "blocked by" candidates for the chosen team.
  tasks: TaskDto[];
  // Team preselected when the sheet opens (e.g. the team currently shown); falls back to the first team.
  defaultTeamId?: string | null;
  // Type the form starts as (the Items screen opens it as Event while its Events tab is showing).
  defaultType?: TaskType;
  onClose: () => void;
  onCreated: () => void;
};

// One "new task" sheet shared by every screen (Dashboard, Items, Calendar, Teams), so a task can be
// added from wherever the user is without a trip to the Items tab.
export function CreateTaskModal({ visible, teams, tasks, defaultTeamId, defaultType = TaskType.Task, onClose, onCreated }: Props) {
  const myUserId = getCurrentUserId();
  const [teamId, setTeamId] = useState<string | null>(null);
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  // Type, template, dates, effort and recurrence live together: they depend on each other (Task vs Event).
  const [schedule, setSchedule] = useState<ScheduleFields>(() => emptySchedule(defaultType));
  const [impactScore, setImpactScore] = useState('');
  const [blockedByTaskId, setBlockedByTaskId] = useState<string | null>(null);
  const [itemListId, setItemListId] = useState<string | null>(null);
  const [assignedUserId, setAssignedUserId] = useState<string | null>(myUserId);
  // Alerts and the people invited besides the assignee (guests of an event / co-contractors of a task).
  const [reminders, setReminders] = useState<ReminderInput[]>([]);
  const [participants, setParticipants] = useState<string[]>([]);
  const [members, setMembers] = useState<TeamMemberDto[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Every time the sheet opens it starts from a clean form on the default team.
  useEffect(() => {
    if (!visible) return;
    setTitle('');
    setDescription('');
    setSchedule(emptySchedule(defaultType));
    setImpactScore('');
    setBlockedByTaskId(null);
    setItemListId(null);
    setAssignedUserId(myUserId);
    setReminders([]);
    setParticipants([]);
    setError(null);
    const preferred = teams.find((t) => t.id === defaultTeamId) ?? teams[0];
    setTeamId(preferred?.id ?? null);
    // Only reset on open; later changes to `teams` must not wipe what the user is typing.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [visible]);

  // The member list is only needed for the assignee picker, so it is loaded while the sheet is open.
  useEffect(() => {
    if (!visible || !teamId) return;
    let cancelled = false;
    setMembers([]);
    getTeamMembers(teamId)
      .then((data) => {
        if (!cancelled) setMembers(data);
      })
      .catch(() => {
        if (!cancelled) setMembers([]);
      });
    return () => {
      cancelled = true;
    };
  }, [visible, teamId]);

  // Owner/Admin of a shared team can hand a new task to any member; everyone else's new task is their own.
  const team = teams.find((t) => t.id === teamId) ?? null;
  const myRole = members.find((m) => m.userId === myUserId)?.role;
  const canAssign = !team?.isPersonal && (myRole === TeamRole.Owner || myRole === TeamRole.Admin);
  // What the chosen kind has: an Out of office / Focus time / Working location / Birthday item has no description (some),
  // no Impact and no blocker - those are PlanFlow's scoring fields and these kinds are not ranked.
  const rules = rulesFor(schedule.taskType, schedule.eventTemplate);
  // Alerts that no longer fit after a change of kind are left out (and come back if the person switches back).
  const reminderContext: ReminderContext = {
    taskType: schedule.taskType,
    allowsAlerts: rules.allowsAlerts,
    isAllDay: schedule.isAllDay,
    hasStart: schedule.startUtc !== null,
    hasDeadline: schedule.deadlineUtc !== null,
  };
  const shownReminders = fitReminders(reminders, reminderContext);
  // Guests of an event / co-contractors of a task need other people: not in a personal workspace, not for a kind without guests.
  const assigneeId = canAssign ? assignedUserId : myUserId;
  const availability = useTeamAvailability(teamId, visible && !team?.isPersonal && members.length > 1);
  const canInvite = !team?.isPersonal && members.length > 1 && (schedule.taskType === TaskType.Task || rules.allowsGuests);
  const shownParticipants = canInvite ? participants.filter((id) => id !== assigneeId) : [];
  // Only items that can really hold something up: not finished, not informational (a birthday, a day off, ...), and a repeating series
  // once (its next occurrence) instead of a copy for every date.
  const blockerChoices = rules.informational
    ? []
    : collapseSeries(tasks.filter((t) => t.teamId === teamId && !isFinishedStatus(t.status) && !isInformational(t)));

  const submit = useCallback(async () => {
    const finalTitle = title.trim() || rules.defaultTitle;
    if (!teamId || !finalTitle) {
      setError('Title is required.');
      return;
    }
    const scheduleError = validateSchedule(schedule, true);
    if (scheduleError) {
      setError(scheduleError);
      return;
    }
    const alertsError = validateReminders(shownReminders, reminderContext);
    if (alertsError) {
      setError(alertsError);
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      const impact = Number(impactScore);

      await createTask(teamId, {
        title: finalTitle,
        description: rules.allowsDescription ? description.trim() || null : null,
        assignedUserId: canAssign ? assignedUserId : myUserId,
        ...scheduleForRequest(schedule),
        impactScore: !rules.informational && impactScore.trim() && !Number.isNaN(impact) ? impact : null, // empty = not rated
        blockedByTaskId: rules.informational ? null : blockedByTaskId,
        reminders: shownReminders,
        participantUserIds: shownParticipants,
        itemListId,
      });
      onCreated();
      onClose();
    } catch (err) {
      setError((err as ApiError).message ?? 'Failed to create.');
    } finally {
      setSubmitting(false);
    }
  }, [teamId, title, description, schedule, rules, impactScore, blockedByTaskId, itemListId, canAssign, assignedUserId, myUserId, shownReminders, shownParticipants, reminderContext, onCreated, onClose]);

  return (
    <Modal transparent animationType="slide" visible={visible} onRequestClose={onClose}>
      <KeyboardAvoidingView style={styles.backdrop} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <Pressable style={styles.dismissArea} onPress={onClose} />
        <View style={styles.sheet}>
          <Text style={styles.heading}>{`New ${rules.label.toLowerCase()}`}</Text>
          <ScrollView keyboardShouldPersistTaps="handled" contentContainerStyle={styles.form}>
            <TextInput
              testID="create-task-title-input"
              style={styles.input}
              placeholder={rules.titlePlaceholder}
              placeholderTextColor={colors.textMuted}
              value={title}
              onChangeText={setTitle}
              autoFocus
            />
            {rules.allowsDescription ? (
              <TextInput
                testID="create-task-description-input"
                style={[styles.input, styles.multiline]}
                placeholder="Description"
                placeholderTextColor={colors.textMuted}
                multiline
                value={description}
                onChangeText={setDescription}
              />
            ) : null}
            <ScheduleForm value={schedule} onChange={setSchedule} isNew />
            {rules.allowsAlerts ? (
              <RemindersEditor value={shownReminders} onChange={setReminders} taskType={schedule.taskType} isAllDay={schedule.isAllDay} />
            ) : null}
            {rules.informational ? null : (
              <>
                <Text style={styles.sectionTitle}>PlanFlow</Text>
                <TextInput
                  testID="create-task-impact-input"
                  style={styles.input}
                  placeholder="Impact score (0-10, empty = not rated)"
                  placeholderTextColor={colors.textMuted}
                  keyboardType="numeric"
                  value={impactScore}
                  onChangeText={setImpactScore}
                />
              </>
            )}

            {teams.length > 1 ? (
              <View>
                <Text style={styles.label}>Team:</Text>
                <View style={styles.chipRow}>
                  {teams.map((t) => (
                    <Pressable
                      key={t.id}
                      testID={`new-task-team-chip-${t.id}`}
                      onPress={() => {
                        setTeamId(t.id);
                        setBlockedByTaskId(null);
                        setItemListId(null);
                        setAssignedUserId(myUserId);
                      }}
                      style={[styles.chip, teamId === t.id ? styles.chipActive : null]}
                    >
                      <Text style={teamId === t.id ? styles.chipTextActive : styles.chipText} numberOfLines={1}>
                        {t.isPersonal ? 'Personal' : t.name}
                      </Text>
                    </Pressable>
                  ))}
                </View>
              </View>
            ) : null}

            <ListPicker
              teamId={teamId}
              kind={schedule.taskType === TaskType.Event ? ItemListKind.MeetingList : ItemListKind.TaskList}
              value={itemListId}
              onChange={setItemListId}
            />

            {canAssign ? <AssigneePicker members={members} value={assignedUserId} onChange={setAssignedUserId} availability={availability} /> : null}

            {canInvite ? (
              <ParticipantPicker
                members={members}
                assigneeId={assigneeId}
                value={shownParticipants}
                onChange={setParticipants}
                label={schedule.taskType === TaskType.Task ? 'Co-contractors' : 'Guests'}
                availability={availability}
              />
            ) : null}

            {blockerChoices.length > 0 ? (
              <View>
                <Text style={styles.label}>Blocked by (optional):</Text>
                <View style={styles.chipRow}>
                  {blockerChoices.map((t) => (
                    <Pressable
                      key={t.id}
                      testID={`blocked-by-chip-${t.id}`}
                      onPress={() => setBlockedByTaskId((current) => (current === t.id ? null : t.id))}
                      style={[styles.chip, blockedByTaskId === t.id ? styles.chipActive : null]}
                    >
                      <Text style={blockedByTaskId === t.id ? styles.chipTextActive : styles.chipText} numberOfLines={1}>
                        {t.title}
                      </Text>
                    </Pressable>
                  ))}
                </View>
              </View>
            ) : null}

            {error ? <Text style={styles.error}>{error}</Text> : null}
          </ScrollView>

          <View style={styles.actions}>
            <Pressable testID="create-task-submit-button" style={styles.button} onPress={submit} disabled={submitting}>
              {submitting ? <ActivityIndicator color={colors.surface} size="small" /> : <Text style={styles.buttonText}>Create</Text>}
            </Pressable>
            <Pressable testID="create-task-cancel-button" onPress={onClose} disabled={submitting}>
              <Text style={styles.label}>Cancel</Text>
            </Pressable>
          </View>
        </View>
      </KeyboardAvoidingView>
    </Modal>
  );
}

const styles = StyleSheet.create({
  backdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.4)', justifyContent: 'flex-end' },
  dismissArea: { flex: 1 },
  sheet: {
    backgroundColor: colors.surface,
    borderTopLeftRadius: radius.lg,
    borderTopRightRadius: radius.lg,
    padding: spacing.lg,
    gap: spacing.sm,
    maxHeight: '85%',
  },
  heading: { ...typography.title, color: colors.text },
  form: { gap: spacing.sm, paddingVertical: spacing.xs },
  input: {
    backgroundColor: colors.background,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.md,
    padding: spacing.md,
    color: colors.text,
  },
  multiline: { minHeight: 72, textAlignVertical: 'top' },
  sectionTitle: { ...typography.subtitle, color: colors.text, marginTop: spacing.xs },
  label: { ...typography.body, color: colors.textMuted },
  error: { ...typography.body, color: colors.danger },
  chipRow: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs, marginTop: spacing.xs },
  chip: {
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.lg,
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xs,
    maxWidth: 160,
  },
  chipActive: { backgroundColor: colors.primaryMuted, borderColor: colors.primary },
  chipText: { ...typography.body, fontSize: 12, color: colors.textMuted },
  chipTextActive: { ...typography.body, fontSize: 12, color: colors.primary, fontWeight: '600' },
  actions: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  button: {
    backgroundColor: colors.primary,
    borderRadius: radius.sm,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.sm,
  },
  buttonText: { color: colors.surface, fontWeight: '700' },
});
