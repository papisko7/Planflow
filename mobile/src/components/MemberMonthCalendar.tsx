import { useCallback, useEffect, useMemo, useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Text, View } from 'react-native';
import { getMemberCalendar } from '../services/availabilityService';
import { colors, radius, spacing, typography } from '../theme/theme';
import { DayAvailability, type MemberCalendarDto } from '../types/availability';
import type { ApiError } from '../types/api';
import { dayKey, startOfDay } from '../utils/dateUtils';
import {
  DAY_COLOR,
  DAY_LABEL,
  entryOnDay,
  entryTime,
  entryTitle,
  formatLoad,
  hoursLabel,
  LOAD_COLOR,
  monthWindow,
  parseApiDay,
  taskDueOnDay,
} from '../utils/availability';
import { MonthGrid } from './MonthGrid';

type Props = {
  teamId: string;
  userId: string;
};

const DAY_TITLE = (d: Date) => d.toLocaleDateString(undefined, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });

// The whole calendar of one teammate, month by month, past and future (read only). One month is requested at a time; what the person
// shares is decided on the server by their own setting, so a month of a person who shares nothing still lists the team items.
export function MemberMonthCalendar({ teamId, userId }: Props) {
  const [month, setMonth] = useState(() => new Date());
  const [selectedDay, setSelectedDay] = useState(() => new Date());
  const [calendar, setCalendar] = useState<MemberCalendarDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);

  const monthKey = `${month.getFullYear()}-${month.getMonth()}`;
  useEffect(() => {
    let cancelled = false;
    const { from, days } = monthWindow(month);
    setCalendar(null);
    setError(null);
    getMemberCalendar(teamId, userId, days, from)
      .then((data) => !cancelled && setCalendar(data))
      .catch((err: ApiError) => !cancelled && setError(err.message ?? "Failed to load this person's calendar."));
    return () => {
      cancelled = true;
    };
    // `month` is represented by monthKey: moving within the same month must not reload.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [teamId, userId, monthKey, attempt]);

  const changeMonth = useCallback((next: Date) => {
    setMonth(next);
    setSelectedDay(new Date(next.getFullYear(), next.getMonth(), 1));
  }, []);

  const markers = useMemo(() => {
    const map = new Map<string, string>();
    if (!calendar) return map;
    for (const day of calendar.member.days) {
      const date = parseApiDay(day.date);
      const hasContent =
        calendar.entries.some((e) => entryOnDay(e, date)) ||
        calendar.tasks.some((t) => taskDueOnDay(t, date)) ||
        calendar.finishedTasks.some((t) => taskDueOnDay(t, date));
      if (day.availability === DayAvailability.NoData || day.availability === DayAvailability.Free) {
        if (hasContent) map.set(dayKey(date), colors.primary);
      } else {
        map.set(dayKey(date), DAY_COLOR[day.availability]);
      }
    }
    return map;
  }, [calendar]);

  const selectedKey = dayKey(selectedDay);
  const dayInfo = calendar?.member.days.find((d) => dayKey(parseApiDay(d.date)) === selectedKey);
  const entries = calendar?.entries.filter((e) => entryOnDay(e, selectedDay)) ?? [];
  const open = calendar?.tasks.filter((t) => taskDueOnDay(t, selectedDay)) ?? [];
  const done = calendar?.finishedTasks.filter((t) => taskDueOnDay(t, selectedDay)) ?? [];
  const isPast = startOfDay(selectedDay) < startOfDay(new Date());
  const empty = entries.length + open.length + done.length === 0;

  return (
    <View testID="member-month-calendar" style={styles.container}>
      <MonthGrid month={month} onMonthChange={changeMonth} selectedKey={selectedKey} markers={markers} onSelectDay={setSelectedDay} />

      {error ? (
        <View style={styles.errorBox}>
          <Text style={styles.error}>{error}</Text>
          <Pressable testID="member-month-retry" onPress={() => setAttempt((n) => n + 1)}>
            <Text style={styles.link}>Try again</Text>
          </Pressable>
        </View>
      ) : null}
      {!calendar && !error ? <ActivityIndicator color={colors.primary} style={styles.spinner} /> : null}

      <View testID={`member-month-day-${selectedKey}`} style={styles.dayBlock}>
        <Text style={styles.dayTitle}>{DAY_TITLE(selectedDay)}</Text>
        {dayInfo ? (
          <View style={styles.dayStatus}>
            <View style={[styles.dot, { backgroundColor: DAY_COLOR[dayInfo.availability] }]} />
            <Text style={styles.footnote}>
              {isPast ? DAY_LABEL[dayInfo.availability] : `${DAY_LABEL[dayInfo.availability]} · ${hoursLabel(dayInfo.loadHours)} planned of ${hoursLabel(dayInfo.capacityHours)}`}
            </Text>
            {isPast ? null : <Text style={[styles.load, { color: LOAD_COLOR[dayInfo.loadLevel] }]}>{formatLoad(dayInfo.loadPercent)}</Text>}
          </View>
        ) : null}

        {entries.map((entry) => (
          <View key={entry.itemId} style={styles.entry}>
            <Text style={styles.entryTime}>{entryTime(entry)}</Text>
            <Text style={styles.entryTitle} numberOfLines={2}>
              {entryTitle(entry)}
              {entry.location ? ` · ${entry.location}` : ''}
            </Text>
            {entry.source === 'Team' ? <Text style={styles.badge}>team</Text> : null}
          </View>
        ))}
        {open.map((task) => (
          <View key={task.itemId} style={styles.entry}>
            <Text style={styles.entryTime}>due</Text>
            <Text style={styles.entryTitle} numberOfLines={2}>{`${task.title} (${hoursLabel(task.effortHours)})`}</Text>
          </View>
        ))}
        {done.map((task) => (
          <View key={task.itemId} style={styles.entry}>
            <Text style={styles.entryTime}>done</Text>
            <Text style={[styles.entryTitle, styles.doneTitle]} numberOfLines={2}>{task.title}</Text>
          </View>
        ))}
        {calendar && empty ? <Text testID="member-month-empty" style={styles.footnote}>Nothing on this day.</Text> : null}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { gap: spacing.sm },
  spinner: { marginVertical: spacing.md },
  errorBox: { gap: spacing.xs },
  error: { ...typography.body, color: colors.danger },
  link: { ...typography.body, color: colors.primary, fontWeight: '600' },
  dayBlock: { backgroundColor: colors.surface, borderRadius: radius.md, padding: spacing.sm, gap: 2 },
  dayTitle: { ...typography.subtitle, color: colors.text },
  dayStatus: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs },
  dot: { width: 10, height: 10, borderRadius: 5 },
  footnote: { ...typography.body, fontSize: 12, color: colors.textMuted, flex: 1 },
  load: { ...typography.body, fontSize: 12, fontWeight: '600' },
  entry: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, paddingVertical: 2 },
  entryTime: { ...typography.body, fontSize: 12, color: colors.textMuted, width: 78 },
  entryTitle: { ...typography.body, color: colors.text, flex: 1 },
  doneTitle: { color: colors.textMuted, textDecorationLine: 'line-through' },
  badge: { ...typography.body, fontSize: 11, color: colors.primary },
});
