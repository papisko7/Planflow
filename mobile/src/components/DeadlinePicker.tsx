import { useState } from 'react';
import { Modal, Pressable, ScrollView, StyleSheet, Text, TextInput, View } from 'react-native';
import { colors, radius, spacing, typography } from '../theme/theme';
import { addDays, composeDeadline, dayKey, formatDay, formatDeadline, splitDeadline, startOfDay } from '../utils/dateUtils';
import { MonthGrid } from './MonthGrid';

type Props = {
  value: string | null;
  onChange: (deadlineUtc: string | null) => void;
  futureOnly?: boolean;
  label?: string;
  optional?: boolean;
  allowAllDay?: boolean;
  minDate?: Date;
  dateOnly?: boolean;
  testIDPrefix?: string;
};

const TIME_CHOICES = ['09:00', '12:00', '15:00', '18:00'];
const CLOCK_TIME = /^([01]?\d|2[0-3]):[0-5]\d$/;

export function DeadlinePicker({
  value,
  onChange,
  futureOnly = false,
  label = 'Deadline',
  optional = true,
  allowAllDay = true,
  minDate,
  dateOnly = false,
  testIDPrefix = 'deadline-picker',
}: Props) {
  const [open, setOpen] = useState(false);
  const [day, setDay] = useState<Date | null>(null);
  const [time, setTime] = useState<string | null>(null); // null = all day
  const [customTime, setCustomTime] = useState('');
  const [month, setMonth] = useState(new Date());

  const openPicker = () => {
    const initial = value ? splitDeadline(value) : null;
    setDay(initial?.day ?? null);
    // Without an "All day" option a new value starts at 09:00; an existing 23:59 stays as a custom time.
    setTime(initial ? (initial.time ?? (allowAllDay ? null : '23:59')) : allowAllDay ? null : '09:00');
    setCustomTime('');
    setMonth(initial?.day ?? new Date());
    setOpen(true);
  };

  const pickDay = (d: Date) => {
    setDay(d);
    setMonth(d);
  };

  const today = startOfDay(new Date());
  const quick = [
    { id: 'today', label: 'Today', date: today },
    { id: 'tomorrow', label: 'Tomorrow', date: addDays(today, 1) },
    { id: 'week', label: 'In a week', date: addDays(today, 7) },
  ];

  // A time already on the task that is not one of the presets (e.g. synced from Google) stays selectable.
  const timeChoices = time && !TIME_CHOICES.includes(time) ? [...TIME_CHOICES, time] : TIME_CHOICES;
  const candidate = day ? composeDeadline(day, dateOnly ? null : time) : null;
  const inPast = futureOnly && candidate !== null && new Date(candidate).getTime() <= Date.now();

  return (
    <>
      <Pressable testID={`${testIDPrefix}-button`} style={styles.field} onPress={openPicker}>
        <Text style={value ? styles.fieldText : styles.fieldPlaceholder}>
          {value ? `${label}: ${dateOnly ? formatDay(value) : formatDeadline(value)}` : optional ? `${label} (optional)` : label}
        </Text>
      </Pressable>

      <Modal transparent animationType="fade" visible={open} onRequestClose={() => setOpen(false)}>
        <Pressable style={styles.backdrop} onPress={() => setOpen(false)}>
          <Pressable style={styles.sheet} onPress={() => undefined}>
            <ScrollView keyboardShouldPersistTaps="handled">
              <Text style={styles.heading}>{label}</Text>

              <View style={styles.chipRow}>
                {quick.map((q) => (
                  <Pressable
                    key={q.id}
                    testID={`${testIDPrefix}-quick-${q.id}`}
                    style={styles.chip}
                    onPress={() => pickDay(q.date)}
                  >
                    <Text style={styles.chipText}>{q.label}</Text>
                  </Pressable>
                ))}
              </View>

              <MonthGrid
                month={month}
                onMonthChange={setMonth}
                selectedKey={day ? dayKey(day) : null}
                minDate={minDate ?? (futureOnly ? today : undefined)}
                onSelectDay={pickDay}
              />

              {dateOnly ? null : (
                <>
                  <Text style={styles.label}>Time</Text>
                  <View style={styles.chipRow}>
                    {allowAllDay ? (
                      <Pressable
                        testID={`${testIDPrefix}-time-allday`}
                        style={[styles.chip, time === null ? styles.chipActive : null]}
                        onPress={() => setTime(null)}
                      >
                        <Text style={time === null ? styles.chipTextActive : styles.chipText}>All day</Text>
                      </Pressable>
                    ) : null}
                    {timeChoices.map((t) => (
                      <Pressable
                        key={t}
                        testID={`${testIDPrefix}-time-${t.replace(':', '')}`}
                        style={[styles.chip, time === t ? styles.chipActive : null]}
                        onPress={() => setTime(t)}
                      >
                        <Text style={time === t ? styles.chipTextActive : styles.chipText}>{t}</Text>
                      </Pressable>
                    ))}
                  </View>
                  <TextInput
                    testID={`${testIDPrefix}-time-input`}
                    style={styles.timeInput}
                    placeholder="Other time, e.g. 08:30"
                    placeholderTextColor={colors.textMuted}
                    value={customTime}
                    onChangeText={(text) => {
                      setCustomTime(text);
                      if (CLOCK_TIME.test(text.trim())) setTime(text.trim().padStart(5, '0'));
                    }}
                    keyboardType="numbers-and-punctuation"
                    maxLength={5}
                  />
                </>
              )}

              {inPast ? <Text style={styles.error}>That moment has already passed - pick a later time.</Text> : null}

              <View style={styles.actions}>
                <Pressable
                  testID={`${testIDPrefix}-set`}
                  style={[styles.button, !day || inPast ? styles.buttonDisabled : null]}
                  disabled={!day || inPast}
                  onPress={() => {
                    if (candidate) onChange(candidate);
                    setOpen(false);
                  }}
                >
                  <Text style={styles.buttonText}>Set</Text>
                </Pressable>
                {value && optional ? (
                  <Pressable
                    testID={`${testIDPrefix}-clear`}
                    onPress={() => {
                      onChange(null);
                      setOpen(false);
                    }}
                  >
                    <Text style={styles.clear}>Clear {label.toLowerCase()}</Text>
                  </Pressable>
                ) : null}
                <Pressable testID={`${testIDPrefix}-cancel`} onPress={() => setOpen(false)}>
                  <Text style={styles.cancel}>Cancel</Text>
                </Pressable>
              </View>
            </ScrollView>
          </Pressable>
        </Pressable>
      </Modal>
    </>
  );
}

const styles = StyleSheet.create({
  field: {
    backgroundColor: colors.background,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.md,
    padding: spacing.md,
  },
  fieldText: { ...typography.body, color: colors.text },
  fieldPlaceholder: { ...typography.body, color: colors.textMuted },
  backdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.4)', justifyContent: 'flex-end' },
  sheet: {
    backgroundColor: colors.surface,
    borderTopLeftRadius: radius.lg,
    borderTopRightRadius: radius.lg,
    padding: spacing.lg,
    maxHeight: '90%',
  },
  heading: { ...typography.title, color: colors.text, marginBottom: spacing.sm },
  label: { ...typography.body, color: colors.textMuted, marginTop: spacing.md },
  timeInput: {
    backgroundColor: colors.background,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.md,
    padding: spacing.sm,
    color: colors.text,
  },
  chipRow: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs, marginVertical: spacing.sm },
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
  actions: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, marginTop: spacing.md },
  button: { backgroundColor: colors.primary, borderRadius: radius.sm, paddingHorizontal: spacing.lg, paddingVertical: spacing.sm },
  buttonDisabled: { opacity: 0.5 },
  buttonText: { color: colors.surface, fontWeight: '700' },
  clear: { ...typography.body, color: colors.danger },
  cancel: { ...typography.body, color: colors.textMuted },
});
