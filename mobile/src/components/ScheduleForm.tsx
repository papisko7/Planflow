import { Pressable, StyleSheet, Switch, Text, View } from 'react-native';
import { colors, radius, spacing, typography } from '../theme/theme';
import { TaskType, type ScheduleFields } from '../types/task';
import { INTERVAL_UNITS } from '../utils/duration';
import { startOfDay } from '../utils/dateUtils';
import { pairEventTimes } from '../utils/schedule';
import { DeadlinePicker } from './DeadlinePicker';
import { DurationInput } from './DurationInput';

type Props = {
  value: ScheduleFields;
  onChange: (next: ScheduleFields) => void;
  // New items must lie in the future (the API rejects anything else); editing an old one may keep a past date.
  isNew: boolean;
};

const TYPE_OPTIONS = [
  { type: TaskType.Task, label: 'Task', testID: 'schedule-type-task' },
  { type: TaskType.Event, label: 'Event', testID: 'schedule-type-event' },
] as const;

// The scheduling part of the create/edit forms, shared by both. A Task is work with ONE deadline and an effort
// estimate; an Event occupies a time range (start - end). Whatever is not relevant for the chosen type is hidden
// (and not sent, see normalizeSchedule). Recurrence applies to both.
export function ScheduleForm({ value, onChange, isNew }: Props) {
  const isEvent = value.taskType === TaskType.Event;
  const patch = (changes: Partial<ScheduleFields>) => onChange({ ...value, ...changes });

  return (
    <View style={styles.wrapper}>
      <View style={styles.segment}>
        {TYPE_OPTIONS.map((option) => (
          <Pressable
            key={option.type}
            testID={option.testID}
            onPress={() => patch({ taskType: option.type })}
            style={[styles.segmentItem, value.taskType === option.type ? styles.segmentItemActive : null]}
          >
            <Text style={value.taskType === option.type ? styles.segmentTextActive : styles.segmentText}>{option.label}</Text>
          </Pressable>
        ))}
      </View>

      {isEvent ? (
        <>
          <DeadlinePicker
            testIDPrefix="event-start-picker"
            label="Start"
            optional={false}
            allowAllDay={false}
            value={value.startUtc}
            onChange={(startUtc) => patch(pairEventTimes(startUtc, value.endUtc, 'start'))}
          />
          <DeadlinePicker
            testIDPrefix="event-end-picker"
            label="End"
            optional={false}
            allowAllDay={false}
            minDate={value.startUtc ? startOfDay(new Date(value.startUtc)) : undefined}
            value={value.endUtc}
            onChange={(endUtc) => patch(pairEventTimes(value.startUtc, endUtc, 'end'))}
          />
        </>
      ) : (
        <>
          <DeadlinePicker futureOnly={isNew} value={value.deadlineUtc} onChange={(deadlineUtc) => patch({ deadlineUtc })} />
          <DurationInput
            testID="effort"
            label="Estimated effort (optional)"
            hours={value.estimatedEffortHours}
            onChange={(estimatedEffortHours) => patch({ estimatedEffortHours })}
          />
        </>
      )}

      <View style={styles.switchRow}>
        <Text style={styles.switchLabel}>Repeats</Text>
        <Switch
          testID="schedule-recurring-switch"
          value={value.isRecurring}
          onValueChange={(isRecurring) => patch({ isRecurring })}
          trackColor={{ true: colors.primary }}
        />
      </View>
      {value.isRecurring ? (
        <>
          <DurationInput
            testID="recurrence-interval"
            label="Repeat every"
            hours={value.recurrenceIntervalHours}
            units={INTERVAL_UNITS}
            defaultUnit="day"
            onChange={(recurrenceIntervalHours) => patch({ recurrenceIntervalHours })}
          />
          <DeadlinePicker
            testIDPrefix="recurrence-end-picker"
            label="Repeat until"
            allowAllDay
            value={value.recurrenceEndsAtUtc}
            onChange={(recurrenceEndsAtUtc) => patch({ recurrenceEndsAtUtc })}
          />
        </>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: { gap: spacing.sm },
  segment: {
    flexDirection: 'row',
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.md,
    overflow: 'hidden',
  },
  segmentItem: { flex: 1, alignItems: 'center', paddingVertical: spacing.sm, backgroundColor: colors.background },
  segmentItemActive: { backgroundColor: colors.primaryMuted },
  segmentText: { ...typography.body, color: colors.textMuted },
  segmentTextActive: { ...typography.body, color: colors.primary, fontWeight: '700' },
  switchRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  switchLabel: { ...typography.body, color: colors.text },
});
