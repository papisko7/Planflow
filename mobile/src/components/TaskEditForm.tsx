import { useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Text, TextInput, View } from 'react-native';
import { updateTask } from '../services/taskService';
import { colors, radius, spacing, typography } from '../theme/theme';
import { useTeamAvailability } from '../hooks/useTeamAvailability';
import { AssigneePicker } from './AssigneePicker';
import { DeadlinePicker } from './DeadlinePicker';
import { DurationInput } from './DurationInput';
import { ListPicker } from './ListPicker';
import { ParticipantPicker } from './ParticipantPicker';
import { RemindersEditor } from './RemindersEditor';
import { ScheduleForm } from './ScheduleForm';
import type { ApiError } from '../types/api';
import type { TeamMemberDto } from '../types/team';
import { ItemListKind } from '../types/itemList';
import { TaskType, type ReminderDto, type ReminderInput, type ScheduleFields, type SeriesScope, type TaskDto } from '../types/task';
import { formatWhen } from '../utils/itemDisplay';
import {
  pickerToWireDay,
  scheduleForEditing,
  scheduleForRequest,
  toUpdateRequest,
  validateEffort,
  validateSchedule,
  wireDayToPicker,
} from '../utils/schedule';
import { recurrenceChanged } from '../utils/recurrence';
import { fitReminders, toReminderInput, validateReminders, type ReminderContext } from '../utils/reminders';
import { askSeriesScope } from '../utils/seriesScope';
import { rulesFor } from '../utils/templateRules';
import { normalizeStatus, STATUS_LABEL, statusesFor } from '../utils/taskStatus';

type Props = {
  task: TaskDto;
  members: TeamMemberDto[];
  // Owner/Admin may reassign; a Member keeps the task on themselves (the API rejects anything else).
  canAssign: boolean;
  // The item's alerts as the detail view loaded them (the task itself does not carry them).
  reminders?: ReminderDto[];
  onSaved: () => void;
  onCancel: () => void;
};

export function TaskEditForm({ task, members, canAssign, reminders, onSaved, onCancel }: Props) {
  const [title, setTitle] = useState(task.title);
  const [description, setDescription] = useState(task.description ?? '');
  const [schedule, setSchedule] = useState<ScheduleFields>(() => scheduleForEditing(task));
  const [impact, setImpact] = useState(task.impactScore === null ? "" : String(task.impactScore));
  const [pickedStatus, setPickedStatus] = useState(task.status);
  // A Task and an Event have different statuses; switching the type in the form swaps the list and resets an illegal pick.
  const status = normalizeStatus(schedule.taskType, pickedStatus);
  const [assignedUserId, setAssignedUserId] = useState(task.assignedUserId);
  const [itemListId, setItemListId] = useState<string | null>(task.itemListId ?? null);
  const [saving, setSaving] = useState(false);
  const rules = rulesFor(schedule.taskType, schedule.eventTemplate);
  // An item imported from Google: Google owns its title, time, place, description and colour (the next sync would overwrite
  // them, and the server ignores edits), so they are shown read-only here; what PlanFlow adds stays editable.
  const imported = task.isExternal;
  // A task from Google Tasks: Google owns its title, notes and due day; PlanFlow adds when to start, effort and impact.
  const googleTask = imported && task.taskType === TaskType.Task;
  // An imported item keeps Google's schedule, but the effort (and, for a Google task, the start) are PlanFlow's own to set.
  // A Google task's days are stored as midnight UTC; the date pickers work with local timestamps, hence the conversions.
  const [effort, setEffort] = useState<number | null>(task.estimatedEffortHours);
  const [start, setStart] = useState<string | null>(() => wireDayToPicker(task.startUtc));
  const [deadline, setDeadline] = useState<string | null>(() => wireDayToPicker(task.deadlineUtc));
  const [alerts, setAlerts] = useState<ReminderInput[]>(() => (reminders ?? []).map(toReminderInput));
  const [participants, setParticipants] = useState<string[]>(task.participantUserIds ?? []);
  const [error, setError] = useState<string | null>(null);

  // An imported event's alerts and people are Google's (shown on the detail screen, not edited here); a task from Google Tasks has
  // none in Google, so its alerts are the person's own.
  const ownAlerts = rules.allowsAlerts && (!imported || googleTask);
  const reminderContext: ReminderContext = {
    taskType: schedule.taskType,
    allowsAlerts: rules.allowsAlerts,
    isAllDay: schedule.isAllDay,
    hasStart: googleTask ? start !== null : schedule.startUtc !== null,
    hasDeadline: googleTask ? (task.isDeadlineDefaulted ? deadline !== null : task.deadlineUtc !== null) : schedule.deadlineUtc !== null,
  };
  const shownAlerts = fitReminders(alerts, reminderContext);
  const availability = useTeamAvailability(task.teamId, members.length > 1);
  const canInvite = members.length > 1 && (!imported || googleTask) && (schedule.taskType === TaskType.Task || rules.allowsGuests);
  const shownParticipants = canInvite ? participants.filter((id) => id !== assignedUserId) : [];

  const save = async () => {
    const trimmedTitle = title.trim();
    if (!trimmedTitle) {
      setError('Title is required.');
      return;
    }
    // Empty = "not rated": the algorithm then ignores impact instead of treating it as 0.
    const impactNumber = impact.trim() === '' ? null : Number(impact);
    if (impactNumber !== null && (Number.isNaN(impactNumber) || impactNumber < 0 || impactNumber > 10)) {
      setError('Impact must be a number from 0 to 10, or empty.');
      return;
    }

    const startDay = pickerToWireDay(start);
    const deadlineDay = task.isDeadlineDefaulted ? pickerToWireDay(deadline) : task.deadlineUtc;
    const scheduleError = imported
      ? validateEffort(effort) ??
        (googleTask && startDay && deadlineDay && startDay > deadlineDay ? 'The start must not be after the deadline.' : null)
      : validateSchedule(schedule, false);
    if (scheduleError) {
      setError(scheduleError);
      return;
    }
    const alertsError = ownAlerts ? validateReminders(shownAlerts, reminderContext) : null;
    if (alertsError) {
      setError(alertsError);
      return;
    }

    // An item that repeats: which occurrences does this change reach? (this / this and following / all)
    let scope: SeriesScope | undefined;
    if (task.seriesId) {
      const ruleChanged = !imported && recurrenceChanged(task, schedule);
      const choice = await askSeriesScope(
        'Edit repeating item',
        ruleChanged
          ? 'You changed how it repeats. That applies to "This and following" or "All items", not to this item alone.'
          : 'This item repeats. Which ones do you want to change?',
      );
      if (choice === null) return;
      scope = choice;
    }
    const scopePart = scope === undefined ? {} : { scope };

    setSaving(true);
    setError(null);
    try {
      await updateTask(
        task.id,
        task.teamId,
        toUpdateRequest(
          task,
          imported
            ? {
                status,
                assignedUserId,
                itemListId,
                impactScore: rules.informational ? null : impactNumber,
                estimatedEffortHours: rules.informational ? task.estimatedEffortHours : effort,
                ...(googleTask ? { startUtc: startDay, deadlineUtc: deadlineDay } : {}),
                ...(ownAlerts ? { reminders: shownAlerts } : {}),
                ...(canInvite ? { participantUserIds: shownParticipants } : {}),
                ...scopePart,
              }
            : {
                title: trimmedTitle,
                description: rules.allowsDescription ? description.trim() || null : null,
                status,
                assignedUserId,
                itemListId,
                impactScore: rules.informational ? null : impactNumber,
                ...scheduleForRequest(schedule),
                ...(ownAlerts ? { reminders: shownAlerts } : {}),
                ...(canInvite ? { participantUserIds: shownParticipants } : {}),
                ...scopePart,
              },
        ),
      );
      onSaved();
    } catch (err) {
      const apiError = err as ApiError;
      setError(apiError.status === 403 ? "You don't have permission to edit this task." : (apiError.message ?? 'Failed to save.'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <View style={styles.form}>
      {task.isDataIncomplete ? (
        <View testID="edit-task-verify-banner" style={styles.banner}>
          <Text style={styles.bannerText}>
            Please check this item: set its impact{rules.informational ? '' : ' and effort'}, then save to confirm it.
          </Text>
        </View>
      ) : null}

      {imported ? (
        <>
          <View testID="edit-task-imported-notice" style={styles.notice}>
            <Text style={styles.noticeText}>
              {googleTask
                ? 'Imported from Google Tasks. Its title, notes and due day are edited there; here you plan it: when to start, how long it takes and how much it matters.'
                : 'Imported from Google Calendar. Its title, time, place, description and colour are edited there; here you set what PlanFlow adds.'}
            </Text>
          </View>
          <Text style={styles.readOnlyTitle}>{task.title}</Text>
          {formatWhen(task) ? <Text style={styles.label}>{formatWhen(task)}</Text> : null}
        </>
      ) : (
        <>
          <TextInput
            testID="edit-task-title-input"
            style={styles.input}
            placeholder={rules.titlePlaceholder}
            placeholderTextColor={colors.textMuted}
            value={title}
            onChangeText={setTitle}
          />
          {rules.allowsDescription ? (
            <TextInput
              testID="edit-task-description-input"
              style={[styles.input, styles.multiline]}
              placeholder="Description (optional)"
              placeholderTextColor={colors.textMuted}
              multiline
              value={description}
              onChangeText={setDescription}
            />
          ) : null}
          <ScheduleForm value={schedule} onChange={setSchedule} isNew={false} />
        </>
      )}

      {ownAlerts ? (
        <RemindersEditor value={shownAlerts} onChange={setAlerts} taskType={schedule.taskType} isAllDay={schedule.isAllDay} />
      ) : null}

      {rules.informational ? null : (
        <>
          <Text style={styles.sectionTitle}>PlanFlow</Text>
          <TextInput
            testID="edit-task-impact-input"
            style={styles.input}
            placeholder="Impact score (0-10, empty = not rated)"
            placeholderTextColor={colors.textMuted}
            keyboardType="numeric"
            value={impact}
            onChangeText={setImpact}
          />
          {imported ? (
            <>
              <DurationInput testID="effort" label="Estimated effort (optional)" hours={effort} onChange={setEffort} />
              {googleTask ? (
                <>
                  <DeadlinePicker
                    testIDPrefix="task-start-picker"
                    label="Start"
                    allowAllDay={false}
                    dateOnly
                    value={start}
                    onChange={setStart}
                  />
                  {task.isDeadlineDefaulted ? (
                    // Google had no due day, so the deadline is only a placeholder ("tomorrow"): it can be set here until the item is saved.
                    <DeadlinePicker label="Deadline" optional={false} allowAllDay={false} dateOnly value={deadline} onChange={setDeadline} />
                  ) : null}
                </>
              ) : null}
            </>
          ) : null}
        </>
      )}

      <View>
        <Text style={styles.label}>Status:</Text>
        <View style={styles.chipRow}>
          {statusesFor(schedule.taskType).map((value) => (
            <Pressable
              key={value}
              testID={`edit-task-status-${value}`}
              onPress={() => setPickedStatus(value)}
              style={[styles.chip, status === value ? styles.chipActive : null]}
            >
              <Text style={status === value ? styles.chipTextActive : styles.chipText}>{STATUS_LABEL[value]}</Text>
            </Pressable>
          ))}
        </View>
      </View>

      <ListPicker
        teamId={task.teamId}
        kind={schedule.taskType === TaskType.Event ? ItemListKind.MeetingList : ItemListKind.TaskList}
        value={itemListId}
        onChange={setItemListId}
      />

      {canAssign && members.length > 1 ? (
        <AssigneePicker members={members} value={assignedUserId} onChange={setAssignedUserId} availability={availability} />
      ) : null}

      {canInvite ? (
        <ParticipantPicker
          members={members}
          assigneeId={assignedUserId}
          value={shownParticipants}
          onChange={setParticipants}
          label={schedule.taskType === TaskType.Task ? 'Co-contractors' : 'Guests'}
          availability={availability}
        />
      ) : null}

      {error ? <Text style={styles.error}>{error}</Text> : null}

      <View style={styles.actions}>
        <Pressable testID="edit-task-save" style={styles.saveButton} onPress={save} disabled={saving}>
          {saving ? <ActivityIndicator color={colors.surface} size="small" /> : <Text style={styles.saveText}>Save</Text>}
        </Pressable>
        <Pressable testID="edit-task-cancel" onPress={onCancel} disabled={saving}>
          <Text style={styles.label}>Cancel</Text>
        </Pressable>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  form: { gap: spacing.sm },
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
  readOnlyTitle: { ...typography.subtitle, color: colors.text },
  banner: { backgroundColor: colors.primaryMuted, borderRadius: radius.md, padding: spacing.md },
  bannerText: { ...typography.body, color: colors.primary, fontWeight: '600' },
  notice: { backgroundColor: colors.background, borderRadius: radius.md, borderWidth: 1, borderColor: colors.border, padding: spacing.md },
  noticeText: { ...typography.body, color: colors.textMuted },
  label: { ...typography.body, color: colors.textMuted },
  chipRow: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs, marginTop: spacing.xs },
  chip: {
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.lg,
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xs,
  },
  chipActive: { backgroundColor: colors.primaryMuted, borderColor: colors.primary },
  chipText: { ...typography.body, fontSize: 12, color: colors.textMuted },
  chipTextActive: { ...typography.body, fontSize: 12, color: colors.primary, fontWeight: '600' },
  error: { ...typography.body, color: colors.danger },
  actions: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  saveButton: {
    backgroundColor: colors.primary,
    borderRadius: radius.sm,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.xs,
  },
  saveText: { color: colors.surface, fontWeight: '700', fontSize: 12 },
});
