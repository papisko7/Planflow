import { useCallback, useEffect, useMemo, useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Text, View } from 'react-native';
import { getCurrentUserId } from '../services/authService';
import { getMemberCalendar, getTeamAvailability } from '../services/availabilityService';
import { colors, radius, spacing, typography } from '../theme/theme';
import {
  CalendarSharingLevel,
  type MemberAvailabilityDto,
  type MemberCalendarDto,
  type TeamAvailabilityDto,
} from '../types/availability';
import type { ApiError } from '../types/api';
import type { TeamDto } from '../types/team';
import {
  DAY_COLOR,
  DAY_LABEL,
  entryOnDay,
  entryTime,
  entryTitle,
  formatLoad,
  hoursLabel,
  LOAD_COLOR,
  parseApiDay,
  SHARING_LABEL,
  STATUS_COLOR,
  STATUS_LABEL,
  taskDueOnDay,
} from '../utils/availability';

type Props = {
  teams: TeamDto[];
};

const WEEKDAY = (d: Date) => d.toLocaleDateString(undefined, { weekday: 'short' });
const DAY_TITLE = (d: Date) => d.toLocaleDateString(undefined, { weekday: 'long', day: 'numeric', month: 'long' });

// "People" view of the Calendar: who in a team is free, busy or away this week and how loaded they are, and - on tap - one person's
// calendar. What a person shares is decided on the server by their own setting (Settings > Calendar sharing), not here.
export function PeopleView({ teams }: Props) {
  const shared = useMemo(() => teams.filter((t) => !t.isPersonal), [teams]);
  const [teamId, setTeamId] = useState<string | null>(shared[0]?.id ?? null);
  const [grid, setGrid] = useState<TeamAvailabilityDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selectedUserId, setSelectedUserId] = useState<string | null>(null);

  useEffect(() => {
    if (!teamId || !shared.some((t) => t.id === teamId)) setTeamId(shared[0]?.id ?? null);
  }, [shared, teamId]);

  const load = useCallback(async () => {
    if (!teamId) return;
    setError(null);
    try {
      setGrid(await getTeamAvailability(teamId));
    } catch (err) {
      setError((err as ApiError).message ?? 'Failed to load availability.');
    }
  }, [teamId]);

  useEffect(() => {
    setGrid(null);
    setSelectedUserId(null);
    load();
  }, [load]);

  if (shared.length === 0) {
    return (
      <View testID="people-empty" style={styles.empty}>
        <Text style={styles.emptyTitle}>No teammates yet</Text>
        <Text style={styles.emptyText}>Create or join a team to see when your teammates are free.</Text>
      </View>
    );
  }

  const selected = grid?.members.find((m) => m.userId === selectedUserId) ?? null;

  return (
    <View testID="people-view" style={styles.container}>
      {shared.length > 1 ? (
        <View style={styles.teamRow}>
          {shared.map((team) => (
            <Pressable
              key={team.id}
              testID={`people-team-${team.id}`}
              onPress={() => setTeamId(team.id)}
              style={[styles.chip, team.id === teamId ? styles.chipActive : null]}
            >
              <Text style={team.id === teamId ? styles.chipTextActive : styles.chipText} numberOfLines={1}>
                {team.name}
              </Text>
            </Pressable>
          ))}
        </View>
      ) : (
        <Text style={styles.teamName}>{shared[0].name}</Text>
      )}

      {error ? (
        <View style={styles.errorBox}>
          <Text style={styles.error}>{error}</Text>
          <Pressable testID="people-retry" onPress={load}>
            <Text style={styles.link}>Try again</Text>
          </Pressable>
        </View>
      ) : null}
      {!grid && !error ? <ActivityIndicator color={colors.primary} style={styles.spinner} /> : null}

      {grid && selected && teamId ? (
        <MemberCalendarPanel teamId={teamId} member={selected} onBack={() => setSelectedUserId(null)} />
      ) : null}

      {grid && !selected ? (
        <View>
          <View style={styles.weekHeader}>
            <View style={styles.nameCol} />
            <View style={styles.cells}>
              {grid.members[0]?.days.map((d) => (
                <Text key={d.date} style={styles.weekday}>
                  {WEEKDAY(parseApiDay(d.date)).slice(0, 2)}
                </Text>
              ))}
            </View>
          </View>
          {grid.members.map((member) => (
            <MemberRow key={member.userId} member={member} onPress={() => setSelectedUserId(member.userId)} />
          ))}
          <Text style={styles.footnote}>
            Load = planned work (task effort spread up to its deadline) against 8 working hours a day, minus meetings and days off.
          </Text>
        </View>
      ) : null}
    </View>
  );
}

function MemberRow({ member, onPress }: { member: MemberAvailabilityDto; onPress: () => void }) {
  const mine = member.userId === getCurrentUserId();
  return (
    <Pressable testID={`people-row-${member.userId}`} onPress={onPress} style={styles.row}>
      <View style={styles.nameCol}>
        <Text style={styles.name} numberOfLines={1}>
          {mine ? `${member.displayName} (you)` : member.displayName}
        </Text>
        <Text style={[styles.status, { color: STATUS_COLOR[member.currentStatus] }]} numberOfLines={1}>
          {STATUS_LABEL[member.currentStatus]}
          {member.currentWorkingLocation ? ` · ${member.currentWorkingLocation}` : ''}
        </Text>
        <Text style={[styles.load, { color: LOAD_COLOR[member.loadLevel] }]}>
          {`Load ${formatLoad(member.windowLoadPercent)}`}
        </Text>
      </View>
      <View style={styles.cells}>
        {member.days.map((d) => (
          <View
            key={d.date}
            testID={`people-cell-${member.userId}-${d.date.slice(0, 10)}`}
            accessibilityLabel={`${d.date.slice(0, 10)}: ${DAY_LABEL[d.availability]}, load ${formatLoad(d.loadPercent)}`}
            style={[styles.cell, { backgroundColor: DAY_COLOR[d.availability] }]}
          />
        ))}
      </View>
    </Pressable>
  );
}

function MemberCalendarPanel({ teamId, member, onBack }: { teamId: string; member: MemberAvailabilityDto; onBack: () => void }) {
  const [calendar, setCalendar] = useState<MemberCalendarDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setCalendar(null);
    setError(null);
    getMemberCalendar(teamId, member.userId)
      .then((data) => !cancelled && setCalendar(data))
      .catch((err: ApiError) => !cancelled && setError(err.message ?? "Failed to load this person's calendar."));
    return () => {
      cancelled = true;
    };
  }, [teamId, member.userId]);

  const days = calendar?.member.days ?? member.days;

  return (
    <View testID="member-calendar">
      <Pressable testID="member-calendar-back" onPress={onBack}>
        <Text style={styles.link}>{'< Everyone'}</Text>
      </Pressable>
      <Text style={styles.memberTitle}>{member.displayName}</Text>
      <Text style={[styles.status, { color: STATUS_COLOR[member.currentStatus] }]}>{STATUS_LABEL[member.currentStatus]}</Text>
      <Text style={styles.footnote}>
        {member.sharingLevel === CalendarSharingLevel.Off
          ? `${member.displayName} does not share their calendar - only team items are listed.`
          : `Shares: ${SHARING_LABEL[member.sharingLevel].toLowerCase()}.`}
      </Text>
      {member.tasksWithoutEffort > 0 ? (
        <Text style={styles.footnote}>
          {`${member.tasksWithoutEffort} open task${member.tasksWithoutEffort === 1 ? ' has' : 's have'} no effort estimate, so the load is higher than shown.`}
        </Text>
      ) : null}

      {error ? <Text style={styles.error}>{error}</Text> : null}
      {!calendar && !error ? <ActivityIndicator color={colors.primary} style={styles.spinner} /> : null}

      {days.map((day) => {
        const date = parseApiDay(day.date);
        const entries = calendar?.entries.filter((e) => entryOnDay(e, date)) ?? [];
        const tasks = calendar?.tasks.filter((t) => taskDueOnDay(t, date)) ?? [];
        return (
          <View key={day.date} testID={`member-day-${day.date.slice(0, 10)}`} style={styles.dayBlock}>
            <View style={styles.dayHeader}>
              <View style={[styles.dot, { backgroundColor: DAY_COLOR[day.availability] }]} />
              <Text style={styles.dayTitle}>{DAY_TITLE(date)}</Text>
              <Text style={[styles.load, { color: LOAD_COLOR[day.loadLevel] }]}>{formatLoad(day.loadPercent)}</Text>
            </View>
            <Text style={styles.footnote}>
              {`${DAY_LABEL[day.availability]} · ${hoursLabel(day.loadHours)} planned of ${hoursLabel(day.capacityHours)}`}
            </Text>
            {entries.map((entry) => (
              <View key={`${entry.itemId}`} style={styles.entry}>
                <Text style={styles.entryTime}>{entryTime(entry)}</Text>
                <Text style={styles.entryTitle} numberOfLines={2}>
                  {entryTitle(entry)}
                  {entry.location ? ` · ${entry.location}` : ''}
                </Text>
                {entry.source === 'Team' ? <Text style={styles.badge}>team</Text> : null}
              </View>
            ))}
            {tasks.map((task) => (
              <View key={task.itemId} style={styles.entry}>
                <Text style={styles.entryTime}>due</Text>
                <Text style={styles.entryTitle} numberOfLines={2}>
                  {`${task.title} (${hoursLabel(task.effortHours)})`}
                </Text>
              </View>
            ))}
          </View>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { paddingHorizontal: spacing.lg, paddingBottom: spacing.lg, gap: spacing.sm },
  empty: { padding: spacing.lg, alignItems: 'center', gap: spacing.xs },
  emptyTitle: { ...typography.title, color: colors.text },
  emptyText: { ...typography.body, color: colors.textMuted, textAlign: 'center' },
  teamRow: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs },
  teamName: { ...typography.subtitle, color: colors.text },
  chip: { borderWidth: 1, borderColor: colors.border, borderRadius: radius.lg, paddingHorizontal: spacing.md, paddingVertical: spacing.xs, maxWidth: 180 },
  chipActive: { backgroundColor: colors.primaryMuted, borderColor: colors.primary },
  chipText: { ...typography.body, color: colors.textMuted },
  chipTextActive: { ...typography.body, color: colors.primary, fontWeight: '600' },
  spinner: { marginVertical: spacing.md },
  errorBox: { gap: spacing.xs },
  error: { ...typography.body, color: colors.danger },
  link: { ...typography.body, color: colors.primary, fontWeight: '600' },
  weekHeader: { flexDirection: 'row', alignItems: 'center', marginBottom: spacing.xs },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: colors.surface,
    borderRadius: radius.md,
    padding: spacing.sm,
    marginBottom: spacing.xs,
  },
  nameCol: { width: 128 },
  name: { ...typography.subtitle, color: colors.text },
  status: { ...typography.body, fontSize: 12, fontWeight: '600' },
  load: { ...typography.body, fontSize: 12, fontWeight: '600' },
  cells: { flex: 1, flexDirection: 'row', gap: 3 },
  cell: { flex: 1, height: 28, borderRadius: 4 },
  weekday: { flex: 1, textAlign: 'center', ...typography.body, fontSize: 11, color: colors.textMuted },
  footnote: { ...typography.body, fontSize: 12, color: colors.textMuted },
  memberTitle: { ...typography.title, color: colors.text },
  dayBlock: { backgroundColor: colors.surface, borderRadius: radius.md, padding: spacing.sm, gap: 2 },
  dayHeader: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs },
  dot: { width: 10, height: 10, borderRadius: 5 },
  dayTitle: { ...typography.subtitle, color: colors.text, flex: 1 },
  entry: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, paddingVertical: 2 },
  entryTime: { ...typography.body, fontSize: 12, color: colors.textMuted, width: 78 },
  entryTitle: { ...typography.body, color: colors.text, flex: 1 },
  badge: { ...typography.body, fontSize: 11, color: colors.primary },
});
