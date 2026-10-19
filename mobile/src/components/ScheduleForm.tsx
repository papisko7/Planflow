import { useState } from 'react';
import { Pressable, StyleSheet, Switch, Text, TextInput, View } from 'react-native';
import { colors, radius, spacing, typography } from '../theme/theme';
import { RecurrenceFrequency, TaskType, type ScheduleFields } from '../types/task';
import { startOfDay } from '../utils/dateUtils';
import { defaultRecurrence, FREQUENCY_LABEL, MAX_INTERVAL, WEEKDAYS, weekdayBit } from '../utils/recurrence';
import { allDayToTimed, pairAllDayDates, pairEventTimes, switchKind } from '../utils/schedule';
import { ITEM_KINDS, rulesFor, WORKING_LOCATION_PRESETS } from '../utils/templateRules';
import { ColorPicker } from './ColorPicker';
import { DeadlinePicker } from './DeadlinePicker';
import { DurationInput } from './DurationInput';

type Props = {
  value: ScheduleFields;
  onChange: (next: ScheduleFields) => void;
  // New items must lie in the future (the API rejects anything else); editing an old one may keep a past date.
  isNew: boolean;
};

// The scheduling part of the create/edit forms, shared by both. The chips at the top pick the kind of item as in Google
// Calendar (Task, Event, Out of office, Focus time, Working location, Birthday); the rest of the form shows only the fields
// that kind has (utils/templateRules.ts). A Task is work with ONE deadline and an effort estimate; an Event occupies a time
// range, or whole days. Whatever is not relevant is hidden and not sent (see scheduleForRequest). Recurrence applies to all.
export function ScheduleForm({ value, onChange, isNew }: Props) {
  const isEvent = value.taskType === TaskType.Event;
  const rules = rulesFor(value.taskType, value.eventTemplate);
  const patch = (changes: Partial<ScheduleFields>) => onChange({ ...value, ...changes });
  const isActive = (kind: (typeof ITEM_KINDS)[number]) =>
    kind.taskType === value.taskType && (kind.taskType === TaskType.Task || kind.template === value.eventTemplate);

  // Picking weekly starts the days at the weekday of the first occurrence, so the rule fits the date it was set up on.
  const changeFrequency = (frequency: RecurrenceFrequency) => {
    const anchor = isEvent ? value.startUtc : value.deadlineUtc;
    patch({
      recurrenceFrequency: frequency,
      recurrenceWeekdays:
        frequency === RecurrenceFrequency.Weekly ? (value.recurrenceWeekdays ?? (anchor ? weekdayBit(new Date(anchor)) : null)) : value.recurrenceWeekdays,
    });
  };

  const toggleAllDay = (next: boolean) =>
    patch({
      isAllDay: next,
      ...(next ? pairAllDayDates(value.startUtc, value.endUtc, value.startUtc ? 'start' : 'end') : allDayToTimed(value.startUtc)),
    });

  return (
    <View style={styles.wrapper}>
      <View style={styles.chipRow}>
        {ITEM_KINDS.map((kind) => (
          <Pressable
            key={kind.id}
            testID={kind.testID}
            onPress={() => onChange(switchKind(value, kind))}
            style={[styles.chip, isActive(kind) ? styles.chipActive : null]}
          >
            <Text style={isActive(kind) ? styles.chipTextActive : styles.chipText}>{kind.label}</Text>
          </Pressable>
        ))}
      </View>

      {isEvent ? (
        <>
          {rules.allDay === 'optional' ? (
            <View style={styles.switchRow}>
              <Text style={styles.switchLabel}>All-day event</Text>
              <Switch testID="schedule-allday-switch" value={value.isAllDay} onValueChange={toggleAllDay} trackColor={{ true: colors.primary }} />
            </View>
          ) : null}
          {rules.allDay === 'always' ? (
            // A birthday is one day: a single date, the end follows by itself.
            <DeadlinePicker
              testIDPrefix="event-start-picker"
              label="Date"
              optional={false}
              allowAllDay={false}
              dateOnly
              value={value.startUtc}
              onChange={(startUtc) => patch(pairAllDayDates(startUtc, null, 'start'))}
            />
          ) : (
            <>
              <DeadlinePicker
                testIDPrefix="event-start-picker"
                label="Start"
                optional={false}
                allowAllDay={false}
                dateOnly={value.isAllDay}
                value={value.startUtc}
                onChange={(startUtc) =>
                  patch(value.isAllDay ? pairAllDayDates(startUtc, value.endUtc, 'start') : pairEventTimes(startUtc, value.endUtc, 'start'))
                }
              />
              <DeadlinePicker
                testIDPrefix="event-end-picker"
                label="End"
                optional={false}
                allowAllDay={false}
                dateOnly={value.isAllDay}
                minDate={value.startUtc ? startOfDay(new Date(value.startUtc)) : undefined}
                value={value.endUtc}
                onChange={(endUtc) =>
                  patch(value.isAllDay ? pairAllDayDates(value.startUtc, endUtc, 'end') : pairEventTimes(value.startUtc, endUtc, 'end'))
                }
              />
            </>
          )}
        </>
      ) : (
        <>
          {/* The planned start is optional: the closer it gets, the more the score nudges you to begin (never later than the deadline). */}
          <DeadlinePicker
            testIDPrefix="task-start-picker"
            label="Start"
            allowAllDay={false}
            value={value.startUtc}
            onChange={(startUtc) => patch({ startUtc })}
          />
          <DeadlinePicker futureOnly={isNew} value={value.deadlineUtc} onChange={(deadlineUtc) => patch({ deadlineUtc })} />
          <DurationInput
            testID="effort"
            label="Estimated effort (optional)"
            hours={value.estimatedEffortHours}
            onChange={(estimatedEffortHours) => patch({ estimatedEffortHours })}
          />
        </>
      )}

      {rules.location === 'text' ? (
        <TextInput
          testID="schedule-location-input"
          style={styles.input}
          placeholder="Location"
          placeholderTextColor={colors.textMuted}
          value={value.location ?? ''}
          onChangeText={(location) => patch({ location })}
        />
      ) : null}
      {rules.location === 'workingLocation' ? (
        <WorkingLocationField value={value.location} onChange={(location) => patch({ location })} />
      ) : null}

      {rules.busy === 'choice' ? (
        <View>
          <Text style={styles.label}>Show me as:</Text>
          <View style={styles.chipRow}>
            {[
              { busy: true, label: 'Busy', testID: 'schedule-busy' },
              { busy: false, label: 'Free', testID: 'schedule-free' },
            ].map((option) => (
              <Pressable
                key={option.testID}
                testID={option.testID}
                onPress={() => patch({ isBusy: option.busy })}
                style={[styles.chip, value.isBusy === option.busy ? styles.chipActive : null]}
              >
                <Text style={value.isBusy === option.busy ? styles.chipTextActive : styles.chipText}>{option.label}</Text>
              </Pressable>
            ))}
          </View>
        </View>
      ) : null}

      {rules.allowsColor ? <ColorPicker value={value.colorKey} onChange={(colorKey) => patch({ colorKey })} /> : null}

      <View style={styles.switchRow}>
        <Text style={styles.switchLabel}>Repeats</Text>
        <Switch
          testID="schedule-recurring-switch"
          value={value.isRecurring}
          onValueChange={(isRecurring) => patch(isRecurring ? defaultRecurrence(isEvent ? value.startUtc : value.deadlineUtc) : { isRecurring })}
          trackColor={{ true: colors.primary }}
        />
      </View>
      {value.isRecurring ? (
        <>
          <View style={styles.chipRow}>
            {Object.values(RecurrenceFrequency)
              .filter((f): f is RecurrenceFrequency => typeof f === 'number')
              .map((frequency) => (
                <Pressable
                  key={frequency}
                  testID={`recurrence-frequency-${frequency}`}
                  onPress={() => changeFrequency(frequency)}
                  style={[styles.chip, value.recurrenceFrequency === frequency ? styles.chipActive : null]}
                >
                  <Text style={value.recurrenceFrequency === frequency ? styles.chipTextActive : styles.chipText}>
                    {FREQUENCY_LABEL[frequency].chip}
                  </Text>
                </Pressable>
              ))}
          </View>
          <View style={styles.intervalRow}>
            <Text style={styles.label}>Every</Text>
            <TextInput
              testID="recurrence-interval-input"
              style={[styles.input, styles.intervalInput]}
              keyboardType="number-pad"
              maxLength={2}
              value={value.recurrenceInterval === null ? '' : String(value.recurrenceInterval)}
              onChangeText={(text) => {
                const parsed = Number.parseInt(text.replace(/D/g, ''), 10);
                patch({ recurrenceInterval: Number.isNaN(parsed) ? null : Math.min(parsed, MAX_INTERVAL) });
              }}
            />
            <Text style={styles.label}>
              {value.recurrenceFrequency === null ? '' : `${FREQUENCY_LABEL[value.recurrenceFrequency].unit}${value.recurrenceInterval === 1 ? '' : 's'}`}
            </Text>
          </View>
          {value.recurrenceFrequency === RecurrenceFrequency.Weekly ? (
            <View style={styles.chipRow}>
              {WEEKDAYS.map((day) => {
                const on = ((value.recurrenceWeekdays ?? 0) & day.bit) !== 0;
                return (
                  <Pressable
                    key={day.bit}
                    testID={`recurrence-day-${day.short.toLowerCase()}`}
                    onPress={() => patch({ recurrenceWeekdays: (value.recurrenceWeekdays ?? 0) ^ day.bit })}
                    style={[styles.chip, on ? styles.chipActive : null]}
                  >
                    <Text style={on ? styles.chipTextActive : styles.chipText}>{day.short}</Text>
                  </Pressable>
                );
              })}
            </View>
          ) : null}
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

// Working location: "Home", "Office" or a label of your own, as in Google Calendar.
function WorkingLocationField({ value, onChange }: { value: string | null; onChange: (location: string | null) => void }) {
  const isPreset = (WORKING_LOCATION_PRESETS as readonly string[]).includes(value ?? '');
  // The custom field is open when the stored label is not one of the presets, or after tapping "Other" with nothing typed yet.
  const [custom, setCustom] = useState(() => Boolean(value) && !isPreset);
  return (
    <View>
      <Text style={styles.label}>Working from:</Text>
      <View style={styles.chipRow}>
        {WORKING_LOCATION_PRESETS.map((preset) => (
          <Pressable
            key={preset}
            testID={`schedule-working-${preset.toLowerCase()}`}
            onPress={() => {
              setCustom(false);
              onChange(preset);
            }}
            style={[styles.chip, !custom && value === preset ? styles.chipActive : null]}
          >
            <Text style={!custom && value === preset ? styles.chipTextActive : styles.chipText}>{preset}</Text>
          </Pressable>
        ))}
        <Pressable
          testID="schedule-working-other"
          onPress={() => {
            setCustom(true);
            if (isPreset) onChange('');
          }}
          style={[styles.chip, custom ? styles.chipActive : null]}
        >
          <Text style={custom ? styles.chipTextActive : styles.chipText}>Other</Text>
        </Pressable>
      </View>
      {custom ? (
        <TextInput
          testID="schedule-location-input"
          style={[styles.input, styles.customInput]}
          placeholder="Label, e.g. Client site"
          placeholderTextColor={colors.textMuted}
          value={value ?? ''}
          onChangeText={onChange}
        />
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: { gap: spacing.sm },
  input: {
    backgroundColor: colors.background,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.md,
    padding: spacing.md,
    color: colors.text,
  },
  customInput: { marginTop: spacing.xs },
  label: { ...typography.body, color: colors.textMuted },
  chipRow: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs },
  chip: {
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.lg,
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xs,
    backgroundColor: colors.background,
  },
  chipActive: { backgroundColor: colors.primaryMuted, borderColor: colors.primary },
  chipText: { ...typography.body, fontSize: 12, color: colors.textMuted },
  chipTextActive: { ...typography.body, fontSize: 12, color: colors.primary, fontWeight: '700' },
  switchRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  switchLabel: { ...typography.body, color: colors.text },
  intervalRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  intervalInput: { width: 64, textAlign: 'center', paddingVertical: spacing.xs },
});
