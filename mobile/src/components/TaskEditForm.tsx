import { useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Text, TextInput, View } from 'react-native';
import { updateTask } from '../services/taskService';
import { colors, radius, spacing, typography } from '../theme/theme';
import { useTeamAvailability } from '../hooks/useTeamAvailability';
import { AssigneePicker } from './AssigneePicker';
import { DeadlinePicker } from './DeadlinePicker';
import { DurationInput } from './DurationInput';
import { FieldLabel } from './FieldLabel';
import { ListPicker } from './ListPicker';
import { ParticipantPicker } from './ParticipantPicker';
import { RemindersEditor } from './RemindersEditor';
import { ScheduleForm } from './ScheduleForm';
import type { ApiError } from '../types/api';
import type { TeamMemberDto } from '../types/team';
import { ItemListKind } from '../types/itemList';
import { TaskType, type ReminderDto, type ReminderInput, type ScheduleFields, type SeriesScope, type TaskDto } from '../types/task';
import { EFFORT_LABEL, IMPACT_HINT, IMPACT_LABEL, TASK_EFFORT_HINT } from '../utils/fieldHints';
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
import { rulesFor, sendsEditsToGoogle } from '../utils/templateRules';
import { normalizeStatus, STATUS_LABEL, statusesFor } from '../utils/taskStatus';

type Props = {
  task: TaskDto;
  members: TeamMemberDto[];
  canAssign: boolean;
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
  const status = normalizeStatus(schedule.taskType, pickedStatus);
  const [assignedUserId, setAssignedUserId] = useState(task.assignedUserId);
  const [itemListId, setItemListId] = useState<string | null>(task.itemListId ?? null);
  const [saving, setSaving] = useState(false);
  const rules = rulesFor(schedule.taskType, schedule.eventTemplate);
  // An item imported from Google is edited like any other, except for its kind (task / event / template) and its repeat rule, which are
  // Google's. The next sync sends the edit to Google; if the item was also changed there, the later of the two changes wins.
  const imported = task.isExternal;
  const sentToGoogle = imported && sendsEditsToGoogle(task);
  const googleTask = imported && task.taskType === TaskType.Task;
  // A Google task keeps whole days (midnight UTC); the date pickers work with local timestamps, hence the conversions.
  const [effort, setEffort] = useState<number | null>(task.estimatedEffortHours);
  const [start, setStart] = useState<string | null>(() => wireDayToPicker(task.startUtc));
  const [deadline, setDeadline] = useState<string | null>(() => wireDayToPicker(task.deadlineUtc));
  const [alerts, setAlerts] = useState<ReminderInput[]>(() => (reminders ?? []).map(toReminderInput));
  const [participants, setParticipants] = useState<string[]>(task.participantUserIds ?? []);
  const [error, setError] = useState<string | null>(null);

  const allowsDescription = googleTask || rules.allowsDescription;
  const reminderContext: ReminderContext = {
    taskType: schedule.taskType,
    allowsAlerts: rules.allowsAlerts,
    isAllDay: schedule.isAllDay,
    hasStart: googleTask ? start !== null : schedule.startUtc !== null,
    hasDeadline: googleTask ? deadline !== null : schedule.deadlineUtc !== null,
  };
  const shownAlerts = fitReminders(alerts, reminderContext);
  const availability = useTeamAvailability(task.teamId, members.length > 1);
  const canInvite = members.length > 1 && (schedule.taskType === TaskType.Task || rules.allowsGuests);
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
    const deadlineDay = pickerToWireDay(deadline);
    const scheduleError = googleTask
      ? validateEffort(effort) ?? (startDay && deadlineDay && startDay > deadlineDay ? 'The start must not be after the deadline.' : null)
      : validateSchedule(schedule, false);
    if (scheduleError) {
      setError(scheduleError);
      return;
    }
    const alertsError = rules.allowsAlerts ? validateReminders(shownAlerts, reminderContext) : null;
    if (alertsError) {
      setError(alertsError);
      return;
    }

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
        toUpdateRequest(task, {
          title: trimmedTitle,
          description: allowsDescription ? description.trim() || null : null,
          status,
          assignedUserId,
          itemListId,
          impactScore: rules.informational ? null : impactNumber,
          ...(googleTask
            ? { estimatedEffortHours: effort, startUtc: startDay, deadlineUtc: deadlineDay }
            : scheduleForRequest(schedule)),
          ...(rules.allowsAlerts ? { reminders: shownAlerts } : {}),
          ...(canInvite ? { participantUserIds: shownParticipants } : {}),
          ...scopePart,
        }),
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
          <Text style={styles.bannerText}>Please check this item: set its impact and effort, then save to confirm it.</Text>
        </View>
      ) : null}

      {imported ? (
        <View testID="edit-task-imported-notice" style={styles.notice}>
          <Text style={styles.noticeText}>
            {googleTask ? 'Imported from Google Tasks.' : 'Imported from Google Calendar.'} You can change everything here.{' '}
            {sentToGoogle
              ? 'Your changes are sent to Google at the next sync. If the item was also changed in Google, the later change wins.'
              : "Google keeps this kind of event for itself, so your changes stay in PlanFlow, and Google's version replaces them if it changes."}
          </Text>
          {task.hasUnsentChanges ? (
            <Text testID="edit-task-unsent-notice" style={styles.noticeText}>
              Your earlier changes have not been sent to Google yet.
            </Text>
          ) : null}
        </View>
      ) : null}

      <View>
        <FieldLabel title={rules.titleLabel} />
        <TextInput testID="edit-task-title-input" style={styles.input} value={title} onChangeText={setTitle} />
      </View>
      {allowsDescription ? (
        <View>
          <FieldLabel title="Description (optional)" hint="Notes, links or anything worth remembering." />
          <TextInput
            testID="edit-task-description-input"
            style={[styles.input, styles.multiline]}
            multiline
            value={description}
            onChangeText={setDescription}
          />
        </View>
      ) : null}

      {googleTask ? (
        <>
          <DeadlinePicker
            testIDPrefix="task-start-picker"
            label="Start"
            hint="When you plan to begin the work."
            allowAllDay={false}
            dateOnly
            value={start}
            onChange={setStart}
          />
          <DeadlinePicker
            label="Deadline"
            hint="When the work has to be finished."
            optional={false}
            allowAllDay={false}
            dateOnly
            value={deadline}
            onChange={setDeadline}
          />
          <DurationInput testID="effort" label={EFFORT_LABEL} hint={TASK_EFFORT_HINT} hours={effort} onChange={setEffort} />
        </>
      ) : (
        <ScheduleForm value={schedule} onChange={setSchedule} isNew={false} lockKind={imported} hideRepeat={imported} />
      )}

      {rules.allowsAlerts ? (
        <RemindersEditor value={shownAlerts} onChange={setAlerts} taskType={schedule.taskType} isAllDay={schedule.isAllDay} />
      ) : null}

      {rules.informational ? null : (
        <>
          <Text style={styles.sectionTitle}>PlanFlow</Text>
          <View>
            <FieldLabel title={IMPACT_LABEL} hint={IMPACT_HINT} />
            <TextInput testID="edit-task-impact-input" style={styles.input} keyboardType="numeric" value={impact} onChangeText={setImpact} />
          </View>
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
