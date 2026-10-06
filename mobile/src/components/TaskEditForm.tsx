import { useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Text, TextInput, View } from 'react-native';
import { updateTask } from '../services/taskService';
import { colors, radius, spacing, typography } from '../theme/theme';
import { AssigneePicker } from './AssigneePicker';
import { DeadlinePicker } from './DeadlinePicker';
import type { ApiError } from '../types/api';
import type { TeamMemberDto } from '../types/team';
import { TaskStatus, type TaskDto } from '../types/task';

type Props = {
  task: TaskDto;
  members: TeamMemberDto[];
  // Owner/Admin may reassign; a Member keeps the task on themselves (the API rejects anything else).
  canAssign: boolean;
  onSaved: () => void;
  onCancel: () => void;
};

const STATUS_OPTIONS: { value: TaskStatus; label: string }[] = [
  { value: TaskStatus.Todo, label: 'To Do' },
  { value: TaskStatus.InProgress, label: 'In Progress' },
  { value: TaskStatus.Blocked, label: 'Blocked' },
  { value: TaskStatus.Done, label: 'Done' },
  { value: TaskStatus.Cancelled, label: 'Cancelled' },
];

export function TaskEditForm({ task, members, canAssign, onSaved, onCancel }: Props) {
  const [title, setTitle] = useState(task.title);
  const [description, setDescription] = useState(task.description ?? '');
  const [deadlineUtc, setDeadlineUtc] = useState<string | null>(task.deadlineUtc);
  const [impact, setImpact] = useState(task.impactScore === null ? "" : String(task.impactScore));
  const [status, setStatus] = useState(task.status);
  const [assignedUserId, setAssignedUserId] = useState(task.assignedUserId);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

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

    setSaving(true);
    setError(null);
    try {
      await updateTask(task.id, task.teamId, {
        title: trimmedTitle,
        description: description.trim() || null,
        status,
        assignedUserId,
        deadlineUtc,
        impactScore: impactNumber,
        blockedByTaskId: task.blockedByTaskId,
        manualUrgencyOverride: task.manualUrgencyOverride,
        manualUrgencyOverrideExpiresAtUtc: task.manualUrgencyOverrideExpiresAtUtc,
      });
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
      <TextInput
        testID="edit-task-title-input"
        style={styles.input}
        placeholder="Title"
        placeholderTextColor={colors.textMuted}
        value={title}
        onChangeText={setTitle}
      />
      <TextInput
        testID="edit-task-description-input"
        style={[styles.input, styles.multiline]}
        placeholder="Description (optional)"
        placeholderTextColor={colors.textMuted}
        multiline
        value={description}
        onChangeText={setDescription}
      />
      <DeadlinePicker value={deadlineUtc} onChange={setDeadlineUtc} />
      <TextInput
        testID="edit-task-impact-input"
        style={styles.input}
        placeholder="Impact score (0-10, empty = not rated)"
        placeholderTextColor={colors.textMuted}
        keyboardType="numeric"
        value={impact}
        onChangeText={setImpact}
      />

      <View>
        <Text style={styles.label}>Status:</Text>
        <View style={styles.chipRow}>
          {STATUS_OPTIONS.map((option) => (
            <Pressable
              key={option.value}
              testID={`edit-task-status-${option.value}`}
              onPress={() => setStatus(option.value)}
              style={[styles.chip, status === option.value ? styles.chipActive : null]}
            >
              <Text style={status === option.value ? styles.chipTextActive : styles.chipText}>{option.label}</Text>
            </Pressable>
          ))}
        </View>
      </View>

      {canAssign && members.length > 1 ? (
        <AssigneePicker members={members} value={assignedUserId} onChange={setAssignedUserId} />
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
