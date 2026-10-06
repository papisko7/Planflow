import { Pressable, StyleSheet, Text, View } from 'react-native';
import { colors, radius, spacing, typography } from '../theme/theme';
import { buildMonthGrid, dayKey, startOfDay } from '../utils/dateUtils';

type Props = {
  // Any date inside the month to show.
  month: Date;
  onMonthChange: (month: Date) => void;
  selectedKey?: string | null;
  // dayKey -> dot color, e.g. the urgency of the most pressing task due that day.
  markers?: Map<string, string>;
  // Days before this one are shown greyed out and cannot be picked.
  minDate?: Date;
  onSelectDay: (day: Date) => void;
};

const WEEKDAYS = ['Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa', 'Su'];

// One month as a tappable grid. Shared by the deadline picker and the Calendar's month view.
export function MonthGrid({ month, onMonthChange, selectedKey, markers, minDate, onSelectDay }: Props) {
  const year = month.getFullYear();
  const monthIndex = month.getMonth();
  const weeks = buildMonthGrid(year, monthIndex);
  const todayKey = dayKey(new Date());
  const minTime = minDate ? startOfDay(minDate).getTime() : null;

  return (
    <View testID="month-grid">
      <View style={styles.header}>
        <Pressable testID="month-grid-prev" hitSlop={12} onPress={() => onMonthChange(new Date(year, monthIndex - 1, 1))}>
          <Text style={styles.arrow}>‹</Text>
        </Pressable>
        <Text testID="month-grid-title" style={styles.title}>
          {month.toLocaleDateString(undefined, { month: 'long', year: 'numeric' })}
        </Text>
        <Pressable testID="month-grid-next" hitSlop={12} onPress={() => onMonthChange(new Date(year, monthIndex + 1, 1))}>
          <Text style={styles.arrow}>›</Text>
        </Pressable>
      </View>

      <View style={styles.row}>
        {WEEKDAYS.map((w) => (
          <Text key={w} style={styles.weekday}>
            {w}
          </Text>
        ))}
      </View>

      {weeks.map((week, i) => (
        <View key={i} style={styles.row}>
          {week.map((day, j) => {
            if (!day) return <View key={j} style={styles.cell} />;
            const key = dayKey(day);
            const disabled = minTime !== null && day.getTime() < minTime;
            const selected = key === selectedKey;
            const marker = markers?.get(key);
            return (
              <Pressable
                key={j}
                testID={`month-grid-day-${key}`}
                disabled={disabled}
                onPress={() => onSelectDay(day)}
                style={[styles.cell, selected ? styles.cellSelected : null]}
              >
                <Text
                  style={[
                    styles.dayText,
                    key === todayKey ? styles.dayToday : null,
                    disabled ? styles.dayDisabled : null,
                    selected ? styles.daySelected : null,
                  ]}
                >
                  {day.getDate()}
                </Text>
                {marker ? <View style={[styles.dot, { backgroundColor: selected ? colors.surface : marker }]} /> : null}
              </Pressable>
            );
          })}
        </View>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  header: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', marginBottom: spacing.sm },
  title: { ...typography.subtitle, color: colors.text },
  arrow: { fontSize: 26, color: colors.primary, paddingHorizontal: spacing.md },
  row: { flexDirection: 'row' },
  weekday: { flex: 1, textAlign: 'center', ...typography.body, fontSize: 12, color: colors.textMuted, paddingVertical: spacing.xs },
  cell: { flex: 1, height: 44, alignItems: 'center', justifyContent: 'center', borderRadius: radius.md },
  cellSelected: { backgroundColor: colors.primary },
  dayText: { ...typography.body, color: colors.text },
  dayToday: { color: colors.primary, fontWeight: '700' },
  dayDisabled: { color: colors.border },
  daySelected: { color: colors.surface, fontWeight: '700' },
  dot: { width: 6, height: 6, borderRadius: 3, marginTop: 2 },
});
