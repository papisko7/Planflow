import { useCallback, useEffect, useState } from 'react';
import { ActivityIndicator, Alert, Pressable, RefreshControl, ScrollView, StyleSheet, Text, View } from 'react-native';
import { useNavigation, useRoute, type RouteProp } from '@react-navigation/native';
import { TaskEditForm } from '../components/TaskEditForm';
import { UrgencyBreakdown } from '../components/UrgencyBreakdown';
import { getCurrentUserId } from '../services/authService';
import { deleteTask, getTask, getTeamTasks, leaveTask, updateTask } from '../services/taskService';
import { getTeamMembers } from '../services/teamService';
import { colors, radius, spacing, typography } from '../theme/theme';
import { formatDuration } from '../utils/duration';
import { colorHex, colorLabel } from '../utils/palette';
import { formatColorKey, formatRecurrence, formatReminder, TEMPLATE_LABEL } from '../utils/itemDisplay';
import { formatDay } from '../utils/dateUtils';
import { allDayDays, toUpdateRequest, utcDay } from '../utils/schedule';
import { isInformational } from '../utils/templateRules';
import type { RootStackParamList } from '../navigation/types';
import type { ApiError } from '../types/api';
import { TeamRole, type TeamMemberDto } from '../types/team';
import type { TaskDetailDto, TaskDto } from '../types/task';
import { EventTemplate, TaskStatus, TaskType } from '../types/task';
import { ItemTypeBadge } from '../components/ItemTypeBadge';
import { STATUS_LABEL } from '../utils/taskStatus';

type LoadState = 'loading' | 'ready' | 'error';

function formatDateTime(iso: string | null): string | null {
  if (!iso) return null;
  return new Date(iso).toLocaleString(undefined, { month: 'short', day: 'numeric', year: 'numeric', hour: '2-digit', minute: '2-digit' });
}

// A whole-day value (a Google task is due on a DAY, stored as midnight UTC of it) shows as that date, never as a clock time.
function formatStoredDay(iso: string | null): string | null {
  return iso ? formatDay(utcDay(iso).toISOString()) : null;
}

export default function TaskDetailScreen() {
  const route = useRoute<RouteProp<RootStackParamList, 'TaskDetail'>>();
  const navigation = useNavigation();
  const { taskId, teamId, edit } = route.params;

  const [members, setMembers] = useState<TeamMemberDto[]>([]);
  // `edit` comes from the swipe-left gesture / long-press menu; the API still rejects it for non-owners.
  const [editing, setEditing] = useState(edit === true);
  const [refreshing, setRefreshing] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [state, setState] = useState<LoadState>('loading');
  const [detail, setDetail] = useState<TaskDetailDto | null>(null);
  // Resolved client-side: the backend only exposes the single upstream `blockedByTaskId` on
  // TaskDto and has no "downstream tasks blocked by this one" endpoint, so both directions of
  // the blocking relationship are derived here from the team's task list.
  const [blockingTask, setBlockingTask] = useState<TaskDto | null>(null);
  const [blockedTasks, setBlockedTasks] = useState<TaskDto[]>([]);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // `silent` (pull-to-refresh) keeps the current content on screen instead of showing the spinner.
  const load = useCallback(async (silent = false) => {
    if (!silent) setState('loading');
    try {
      const taskDetail = await getTask(taskId, teamId);
      setDetail(taskDetail);

      const teamTasks = await getTeamTasks(taskDetail.task.teamId);
      setBlockingTask(teamTasks.find((t) => t.id === taskDetail.task.blockedByTaskId) ?? null);
      setBlockedTasks(teamTasks.filter((t) => t.blockedByTaskId === taskDetail.task.id));
      setMembers(await getTeamMembers(taskDetail.task.teamId).catch(() => []));

      setState('ready');
    } catch (err) {
      const apiError = err as ApiError;
      setErrorMessage(apiError.message ?? 'Failed to load task.');
      setState('error');
    }
  }, [taskId, teamId]);

  useEffect(() => {
    load();
  }, [load]);

  const onRefresh = useCallback(async () => {
    setRefreshing(true);
    await load(true);
    setRefreshing(false);
  }, [load]);

  if (state === 'loading') {
    return (
      <View style={styles.centered}>
        <ActivityIndicator size="large" color={colors.primary} />
      </View>
    );
  }

  if (state === 'error' || !detail) {
    return (
      <View style={styles.centered}>
        <Text style={styles.title}>Couldn't load task</Text>
        <Text style={styles.subtitle}>{errorMessage}</Text>
      </View>
    );
  }

  const { task, latestScoreBreakdown } = detail;
  const reminders = detail.reminders ?? [];
  const isEvent = task.taskType === TaskType.Event;
  const wholeDayTask = !isEvent && task.isAllDay;
  const dueDate = wholeDayTask ? formatStoredDay(task.deadlineUtc) : formatDateTime(task.deadlineUtc);
  const plannedStart = isEvent ? null : wholeDayTask ? formatStoredDay(task.startUtc) : formatDateTime(task.startUtc);
  const ranked = !isInformational(task);
  // An all-day event reads "Oct 19, 2026 · all day" instead of 12:00 AM - 11:59 PM.
  // (For an imported all-day event the end shown is its LAST day, not the midnight after it that is stored.)
  const days = allDayDays(task);
  const allDay = days !== null;
  const startDate = days ? formatDay(days.first.toISOString()) : formatDateTime(task.startUtc);
  const endDate = days ? formatDay(days.last.toISOString()) : formatDateTime(task.endUtc);
  const templateLabel = TEMPLATE_LABEL[task.eventTemplate];
  const recurrence = formatRecurrence(task);
  const recurrenceEnd = formatDateTime(task.recurrenceEndsAtUtc);
  const createdDate = formatDateTime(task.createdAtUtc);
  const participantIds = task.participantUserIds ?? [];

  // Same rule as the API: Owner/Admin act on any task, a Member only on tasks assigned to them.
  const myUserId = getCurrentUserId();
  const myRole = members.find((m) => m.userId === myUserId)?.role;
  const isPrivileged = myRole === TeamRole.Owner || myRole === TeamRole.Admin;
  const canModify = isPrivileged || (myUserId !== null && task.assignedUserId === myUserId);
  const assigneeName = task.assignedUserId
    ? (members.find((m) => m.userId === task.assignedUserId)?.userDisplayName ?? 'Former member')
    : 'Unassigned';

  // One-tap done / not done; the update endpoint replaces the whole task, so the other fields are resent.
  const toggleDone = async () => {
    setActionError(null);
    try {
      await updateTask(
        task.id,
        task.teamId,
        toUpdateRequest(task, { status: task.status === TaskStatus.Completed ? TaskStatus.Backlog : TaskStatus.Completed }),
      );
      await load(true);
    } catch (err) {
      const apiError = err as ApiError;
      setActionError(apiError.status === 403 ? "You don't have permission to change this task." : (apiError.message ?? 'Failed to update.'));
    }
  };

  // Claim = assign an unassigned task to yourself (a Member can't touch it before, but owns it afterwards).
  // Abandon = the reverse: clear the assignee so the task returns to the unassigned pool.
  const changeAssignee = async (assignedUserId: string | null, failureMessage: string) => {
    setActionError(null);
    try {
      await updateTask(task.id, task.teamId, toUpdateRequest(task, { assignedUserId }));
      await load(true);
    } catch (err) {
      const apiError = err as ApiError;
      setActionError(apiError.message ?? failureMessage);
    }
  };
  const claim = () => (myUserId ? changeAssignee(myUserId, 'Failed to take over the task.') : undefined);
  const abandon = () => changeAssignee(null, 'Failed to abandon the task.');
  const isMine = myUserId !== null && task.assignedUserId === myUserId;

  // The guests of an event / co-contractors of a task, by name; a guest or co-contractor can step out of it (not edit it).
  const participantNames = participantIds.map((id) => members.find((m) => m.userId === id)?.userDisplayName ?? 'Former member');
  const isParticipant = myUserId !== null && participantIds.includes(myUserId);
  const leave = async () => {
    setActionError(null);
    try {
      await leaveTask(task.id, task.teamId);
      await load(true);
    } catch (err) {
      setActionError((err as ApiError).message ?? 'Failed to leave.');
    }
  };

  const confirmDelete = () => {
    Alert.alert(`Delete "${task.title}"?`, undefined, [
      { text: 'Cancel', style: 'cancel' },
      {
        text: 'Delete',
        style: 'destructive',
        onPress: async () => {
          try {
            await deleteTask(task.id, task.teamId);
            navigation.goBack();
          } catch (err) {
            const apiError = err as ApiError;
            setActionError(apiError.status === 403 ? "You don't have permission to delete this task." : (apiError.message ?? 'Failed to delete.'));
          }
        },
      },
    ]);
  };

  return (
    <ScrollView
      style={styles.container}
      contentContainerStyle={styles.content}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={colors.primary} />}
    >
      {editing ? (
        <View style={styles.card}>
          <TaskEditForm
            task={task}
            members={members}
            canAssign={isPrivileged}
            reminders={detail.reminders}
            onCancel={() => setEditing(false)}
            onSaved={() => {
              setEditing(false);
              load();
            }}
          />
        </View>
      ) : null}
      <View style={styles.card}>
        <Text style={styles.taskTitle}>{task.title}</Text>
        <View style={styles.badgeRow}>
          <Text style={styles.statusBadge}>{STATUS_LABEL[task.status]}</Text>
          <ItemTypeBadge testID="task-type-badge" type={task.taskType} />
          {templateLabel ? <Text testID="task-template-badge" style={styles.statusBadge}>{templateLabel}</Text> : null}
          {task.isDataIncomplete ? <Text testID="needs-details-badge" style={styles.overrideBadge}>⚠ Needs details</Text> : null}
          {task.manualUrgencyOverride !== null ? <Text style={styles.overrideBadge}>Manual override</Text> : null}
        </View>
        {task.description ? <Text style={styles.description}>{task.description}</Text> : null}

        <View style={styles.metaGrid}>
          {createdDate ? <MetaItem label="Created" value={createdDate} /> : null}
          {isEvent ? (
            <>
              {startDate ? <MetaItem label="Starts" value={startDate} /> : null}
              {endDate ? <MetaItem label="Ends" value={endDate} /> : null}
              {allDay ? <MetaItem label="Duration" value="All day" /> : null}
            </>
          ) : (
            <>
              {plannedStart ? <MetaItem label="Planned start" value={plannedStart} /> : null}
              {dueDate ? <MetaItem label="Due" value={task.isDeadlineDefaulted ? `${dueDate} (not set in Google)` : dueDate} /> : null}
              {task.estimatedEffortHours !== null ? (
                <MetaItem label="Estimated effort" value={formatDuration(task.estimatedEffortHours)} />
              ) : null}
            </>
          )}
          {task.googleTaskListTitle ? <MetaItem label="Google Tasks list" value={task.googleTaskListTitle} /> : null}
          {task.location ? <MetaItem label="Location" value={task.location} /> : null}
          {isEvent && task.eventTemplate === EventTemplate.Default ? <MetaItem label="Show as" value={task.isBusy ? 'Busy' : 'Free'} /> : null}
          {task.colorKey ? (
            <MetaItem label="Colour" value={colorHex(task.colorKey) ? colorLabel(task.colorKey) : formatColorKey(task.colorKey)} swatch={colorHex(task.colorKey)} />
          ) : null}
          {reminders.length > 0 ? <MetaItem label="Reminders" value={reminders.map((r) => formatReminder(r, task.taskType)).join('\n')} /> : null}
          {task.externalSettings ? <MetaItem label="Google settings" value={task.externalSettings} /> : null}
          {recurrence ? (
            <MetaItem label="Repeats" value={recurrenceEnd ? `${recurrence}, until ${recurrenceEnd}` : recurrence} />
          ) : null}
          {ranked ? <MetaItem label="Impact score" value={task.impactScore === null ? "Not rated" : String(task.impactScore)} /> : null}
          <MetaItem label="Assigned to" value={assigneeName} />
          {participantNames.length > 0 ? <MetaItem label={isEvent ? 'Guests' : 'Co-contractors'} value={participantNames.join(', ')} /> : null}
        </View>

        {!task.assignedUserId && !editing ? (
          <Pressable testID="task-claim-button" style={styles.editButton} onPress={claim}>
            <Text style={styles.editButtonText}>Take over this task</Text>
          </Pressable>
        ) : null}
        {isParticipant && !editing ? (
          <Pressable testID="task-leave-button" style={styles.editButton} onPress={leave}>
            <Text style={styles.editButtonText}>{isEvent ? 'Leave this event' : 'Leave this task'}</Text>
          </Pressable>
        ) : null}
        {isMine && !editing ? (
          <Pressable testID="task-abandon-button" style={styles.editButton} onPress={abandon}>
            <Text style={styles.editButtonText}>Abandon this task</Text>
          </Pressable>
        ) : null}
        {canModify && !editing ? (
          <View style={styles.actionRow}>
            <Pressable testID="task-edit-button" style={styles.editButton} onPress={() => setEditing(true)}>
              <Text style={styles.editButtonText}>Edit</Text>
            </Pressable>
            {/* An event is not "done" — it is Confirmed / Tentative / Cancelled, changed in the edit form. */}
            {isEvent ? null : (
              <Pressable testID="task-toggle-done-button" onPress={toggleDone}>
                <Text style={styles.toggleText}>{task.status === TaskStatus.Completed ? 'Mark as not done' : 'Mark as done'}</Text>
              </Pressable>
            )}
            <Pressable testID="task-delete-button" onPress={confirmDelete}>
              <Text style={styles.deleteText}>Delete</Text>
            </Pressable>
          </View>
        ) : null}
        {actionError ? <Text style={styles.deleteText}>{actionError}</Text> : null}
      </View>

      {/* A day off, a focus block, a working place or a birthday is not ranked: no blocking chain and no score to explain. */}
      {ranked ? (
      <View style={styles.card}>
        <Text style={styles.sectionTitle}>Blocking Relationships</Text>
        {blockingTask ? (
          <Text style={styles.relationText}>⛔ Blocked by: {blockingTask.title}</Text>
        ) : task.blockedByTaskId ? (
          <Text style={styles.relationText}>⛔ Blocked by a task outside this team's list</Text>
        ) : (
          <Text style={styles.relationTextMuted}>Not blocked by any task.</Text>
        )}
        {blockedTasks.length > 0 ? (
          <View style={styles.relationList}>
            <Text style={styles.relationTextMuted}>Blocks {blockedTasks.length} downstream item(s):</Text>
            {blockedTasks.map((t) => (
              <Text key={t.id} style={styles.relationText}>
                • {t.title}
              </Text>
            ))}
          </View>
        ) : null}
      </View>
      ) : null}

      {ranked ? (
      <View style={styles.card}>
        <Text style={styles.sectionTitle}>Urgency Score Breakdown</Text>
        {latestScoreBreakdown ? (
          <UrgencyBreakdown breakdown={latestScoreBreakdown} />
        ) : (
          <Text style={styles.relationTextMuted}>No score has been computed for this task yet.</Text>
        )}
      </View>
      ) : null}
    </ScrollView>
  );
}

function MetaItem({ label, value, swatch }: { label: string; value: string; swatch?: string | null }) {
  return (
    <View style={styles.metaItem}>
      <Text style={styles.metaLabel}>{label}</Text>
      <View style={styles.metaValueRow}>
        {swatch ? <View testID="task-color-swatch" style={[styles.swatch, { backgroundColor: swatch }]} /> : null}
        <Text style={styles.metaValue}>{value}</Text>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  metaValueRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs },
  swatch: { width: 12, height: 12, borderRadius: 6 },
  container: { flex: 1, backgroundColor: colors.background },
  content: { padding: spacing.lg, gap: spacing.md },
  centered: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: spacing.lg, gap: spacing.xs },
  title: { ...typography.title, color: colors.text },
  subtitle: { ...typography.body, color: colors.textMuted, textAlign: 'center' },
  card: {
    backgroundColor: colors.surface,
    borderRadius: radius.md,
    padding: spacing.md,
    gap: spacing.sm,
  },
  taskTitle: { ...typography.title, color: colors.text },
  badgeRow: { flexDirection: 'row', gap: spacing.sm },
  statusBadge: {
    ...typography.body,
    fontSize: 12,
    fontWeight: '700',
    color: colors.primary,
    backgroundColor: colors.primaryMuted,
    paddingHorizontal: spacing.sm,
    paddingVertical: 2,
    borderRadius: radius.sm,
    overflow: 'hidden',
  },
  overrideBadge: {
    ...typography.body,
    fontSize: 12,
    fontWeight: '700',
    color: colors.textMuted,
    borderWidth: 1,
    borderColor: colors.border,
    paddingHorizontal: spacing.sm,
    paddingVertical: 2,
    borderRadius: radius.sm,
    overflow: 'hidden',
  },
  description: { ...typography.body, color: colors.textMuted },
  metaGrid: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.lg, marginTop: spacing.xs },
  metaItem: { gap: 2 },
  metaLabel: { fontSize: 12, color: colors.textMuted },
  metaValue: { ...typography.body, color: colors.text, fontWeight: '600' },
  sectionTitle: { ...typography.subtitle, color: colors.text },
  relationText: { ...typography.body, color: colors.text },
  relationTextMuted: { ...typography.body, color: colors.textMuted },
  relationList: { gap: 2, marginTop: spacing.xs },
  actionRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.lg, marginTop: spacing.sm },
  editButton: {
    backgroundColor: colors.primary,
    borderRadius: radius.sm,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.xs,
  },
  editButtonText: { color: colors.surface, fontWeight: '700', fontSize: 12 },
  toggleText: { ...typography.body, color: colors.primary, fontWeight: '600' },
  deleteText: { ...typography.body, color: colors.danger, fontWeight: '600' },
});
