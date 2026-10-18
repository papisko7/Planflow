import { useState } from 'react';
import { Pressable, StyleSheet, Text, TextInput, View } from 'react-native';
import { colors, radius, spacing, typography } from '../theme/theme';
import { ReminderAnchor, TaskType, type ReminderInput } from '../types/task';
import { formatReminder } from '../utils/itemDisplay';
import {
  dayReminder,
  MAX_DAYS_BEFORE,
  MAX_OFFSET_MINUTES,
  MAX_REMINDERS,
  offsetReminder,
  parseTimeOfDay,
  reminderKey,
} from '../utils/reminders';

type Unit = 'minutes' | 'hours' | 'days';

const UNITS: { id: Unit; label: string; minutes: number }[] = [
  { id: 'minutes', label: 'minutes', minutes: 1 },
  { id: 'hours', label: 'hours', minutes: 60 },
  { id: 'days', label: 'days', minutes: 1440 },
];

type Props = {
  value: ReminderInput[];
  onChange: (next: ReminderInput[]) => void;
  taskType: TaskType;
  // A whole-day item is only reminded some days before, at a time of day ("minutes before midnight" means nothing to a person).
  isAllDay: boolean;
};

// The "Alerts" part of the create/edit forms: up to four reminders, each "N minutes/hours/days before" (or, for a whole-day item,
// "N days before at HH:MM"). A task picks what it counts back from - its planned start or its deadline; an event always counts from its start.
export function RemindersEditor({ value, onChange, taskType, isAllDay }: Props) {
  const isTask = taskType === TaskType.Task;
  // A task is most often reminded before its deadline; an event ignores this (it only has a start).
  const [anchor, setAnchor] = useState<ReminderAnchor>(ReminderAnchor.Deadline);
  const [amount, setAmount] = useState('10');
  const [unit, setUnit] = useState<Unit>('minutes');
  const [time, setTime] = useState('09:00');
  const [error, setError] = useState<string | null>(null);

  // An event can only count from its start, whatever the task chip last said.
  const effectiveAnchor = isTask ? anchor : ReminderAnchor.Start;

  const add = () => {
    const count = Number(amount);
    if (amount.trim() === '' || !Number.isInteger(count) || count < 0) {
      setError('Enter a whole number, 0 or more.');
      return;
    }

    let reminder: ReminderInput;
    if (isAllDay) {
      const timeOfDay = parseTimeOfDay(time);
      if (timeOfDay === null) {
        setError('Enter the time as HH:MM, e.g. 09:00.');
        return;
      }
      if (count > MAX_DAYS_BEFORE) {
        setError(`At most ${MAX_DAYS_BEFORE} days before.`);
        return;
      }
      reminder = dayReminder(count, timeOfDay, effectiveAnchor);
    } else {
      const minutes = count * (UNITS.find((u) => u.id === unit)?.minutes ?? 1);
      if (minutes > MAX_OFFSET_MINUTES) {
        setError(`At most ${MAX_OFFSET_MINUTES / 1440} days before.`);
        return;
      }
      reminder = offsetReminder(minutes, effectiveAnchor);
    }

    if (value.some((r) => reminderKey(r) === reminderKey(reminder))) {
      setError('That alert is already there.');
      return;
    }
    setError(null);
    onChange([...value, reminder]);
  };

  const remove = (index: number) => onChange(value.filter((_, i) => i !== index));

  return (
    <View testID="reminders-editor" style={styles.wrapper}>
      <Text style={styles.sectionTitle}>Alerts</Text>

      {value.length === 0 ? <Text style={styles.hint}>No alerts yet.</Text> : null}
      {value.map((reminder, index) => (
        <View key={reminderKey(reminder)} testID={`reminder-row-${index}`} style={styles.row}>
          <Text style={styles.rowText}>{formatReminder(reminder, taskType)}</Text>
          <Pressable testID={`reminder-remove-${index}`} onPress={() => remove(index)} hitSlop={8}>
            <Text style={styles.remove}>Remove</Text>
          </Pressable>
        </View>
      ))}

      {value.length < MAX_REMINDERS ? (
        <View style={styles.addBox}>
          {isTask ? (
            <View style={styles.chipRow}>
              {[
                { id: ReminderAnchor.Deadline, label: 'Before the deadline', testID: 'reminder-anchor-deadline' },
                { id: ReminderAnchor.Start, label: 'Before the start', testID: 'reminder-anchor-start' },
              ].map((option) => (
                <Pressable
                  key={option.testID}
                  testID={option.testID}
                  onPress={() => setAnchor(option.id)}
                  style={[styles.chip, effectiveAnchor === option.id ? styles.chipActive : null]}
                >
                  <Text style={effectiveAnchor === option.id ? styles.chipTextActive : styles.chipText}>{option.label}</Text>
                </Pressable>
              ))}
            </View>
          ) : null}

          <View style={styles.inputRow}>
            <TextInput
              testID={isAllDay ? 'reminder-days-input' : 'reminder-amount-input'}
              style={[styles.input, styles.amount]}
              keyboardType="numeric"
              value={amount}
              onChangeText={setAmount}
            />
            {isAllDay ? (
              <>
                <Text style={styles.rowText}>days before at</Text>
                <TextInput
                  testID="reminder-time-input"
                  style={[styles.input, styles.time]}
                  placeholder="09:00"
                  placeholderTextColor={colors.textMuted}
                  value={time}
                  onChangeText={setTime}
                />
              </>
            ) : (
              <View style={styles.chipRow}>
                {UNITS.map((option) => (
                  <Pressable
                    key={option.id}
                    testID={`reminder-unit-${option.id}`}
                    onPress={() => setUnit(option.id)}
                    style={[styles.chip, unit === option.id ? styles.chipActive : null]}
                  >
                    <Text style={unit === option.id ? styles.chipTextActive : styles.chipText}>{option.label}</Text>
                  </Pressable>
                ))}
              </View>
            )}
          </View>

          <Pressable testID="reminder-add" onPress={add} style={styles.addButton}>
            <Text style={styles.addText}>Add alert</Text>
          </Pressable>
        </View>
      ) : (
        <Text style={styles.hint}>Four alerts is the most an item can have.</Text>
      )}

      {error ? <Text testID="reminder-error" style={styles.error}>{error}</Text> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: { gap: spacing.xs },
  sectionTitle: { ...typography.subtitle, color: colors.text, marginTop: spacing.xs },
  hint: { ...typography.body, fontSize: 12, color: colors.textMuted },
  row: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: spacing.sm },
  rowText: { ...typography.body, color: colors.text },
  remove: { ...typography.body, fontSize: 12, color: colors.danger, fontWeight: '600' },
  addBox: { gap: spacing.xs },
  inputRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs, flexWrap: 'wrap' },
  input: {
    backgroundColor: colors.background,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.md,
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xs,
    color: colors.text,
  },
  amount: { minWidth: 56, textAlign: 'center' },
  time: { minWidth: 72, textAlign: 'center' },
  chipRow: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs },
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
  addButton: {
    alignSelf: 'flex-start',
    borderWidth: 1,
    borderColor: colors.primary,
    borderRadius: radius.sm,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.xs,
  },
  addText: { color: colors.primary, fontWeight: '700', fontSize: 12 },
  error: { ...typography.body, color: colors.danger },
});
