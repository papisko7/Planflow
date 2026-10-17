import { useState } from 'react';
import { Pressable, StyleSheet, Text, TextInput, View } from 'react-native';
import { colors, radius, spacing, typography } from '../theme/theme';
import { DURATION_UNIT_LABEL, EFFORT_UNITS, fromHours, toHours, type DurationUnit } from '../utils/duration';

type Props = {
  label: string;
  // Stored value in hours (what the API uses) or null when empty.
  hours: number | null;
  onChange: (hours: number | null) => void;
  units?: DurationUnit[];
  // Unit shown when there is no value yet.
  defaultUnit?: DurationUnit;
  testID: string;
};

// A number plus a unit ("2" + Days). The user types in the unit that feels natural; the parent only ever sees hours.
export function DurationInput({ label, hours, onChange, units = EFFORT_UNITS, defaultUnit = 'hour', testID }: Props) {
  const initial = hours !== null ? fromHours(hours, units) : null;
  const [text, setText] = useState(initial ? String(initial.value) : '');
  const [unit, setUnit] = useState<DurationUnit>(initial?.unit ?? defaultUnit);

  const emit = (nextText: string, nextUnit: DurationUnit) => {
    const value = Number(nextText.replace(',', '.'));
    onChange(nextText.trim() === '' || Number.isNaN(value) ? null : toHours(value, nextUnit));
  };

  return (
    <View style={styles.wrapper}>
      <Text style={styles.label}>{label}</Text>
      <TextInput
        testID={`${testID}-input`}
        style={styles.input}
        placeholder="Amount (empty = not set)"
        placeholderTextColor={colors.textMuted}
        keyboardType="decimal-pad"
        value={text}
        onChangeText={(next) => {
          setText(next);
          emit(next, unit);
        }}
      />
      <View style={styles.chipRow}>
        {units.map((u) => (
          <Pressable
            key={u}
            testID={`${testID}-unit-${u}`}
            onPress={() => {
              setUnit(u);
              emit(text, u);
            }}
            style={[styles.chip, unit === u ? styles.chipActive : null]}
          >
            <Text style={unit === u ? styles.chipTextActive : styles.chipText}>{DURATION_UNIT_LABEL[u].long}</Text>
          </Pressable>
        ))}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: { gap: spacing.xs },
  label: { ...typography.body, color: colors.textMuted },
  input: {
    backgroundColor: colors.background,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.md,
    padding: spacing.md,
    color: colors.text,
  },
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
});
