import { Pressable, StyleSheet, Text, View } from 'react-native';
import { getCurrentUserId } from '../services/authService';
import { colors, radius, spacing, typography } from '../theme/theme';
import { LoadLevel, MemberStatus, type MemberAvailabilityDto } from '../types/availability';
import type { TeamMemberDto } from '../types/team';
import { assigneeWarning, pickerHint } from '../utils/availability';

type Props = {
  members: TeamMemberDto[];
  value: string | null;
  onChange: (userId: string | null) => void;
  availability?: Record<string, MemberAvailabilityDto>;
};

export function AssigneePicker({ members, value, onChange, availability }: Props) {
  const myUserId = getCurrentUserId();
  return (
    <View>
      <Text style={styles.label}>Assigned to:</Text>
      <View style={styles.row}>
        <Chip testID="assignee-chip-none" label="Unassigned" active={value === null} onPress={() => onChange(null)} />
        {members.map((m) => {
          const info = availability?.[m.userId];
          const hint = pickerHint(info);
          const name = m.userId === myUserId ? `${m.userDisplayName} (you)` : m.userDisplayName;
          return (
            <Chip
              key={m.userId}
              testID={`assignee-chip-${m.userId}`}
              label={hint ? `${name} · ${hint}` : name}
              active={value === m.userId}
              warn={info?.currentStatus === MemberStatus.OutOfOffice || info?.loadLevel === LoadLevel.Overloaded}
              onPress={() => onChange(m.userId)}
            />
          );
        })}
      </View>
      {value && assigneeWarning(availability?.[value]) ? (
        <Text testID="assignee-warning" style={styles.warning}>
          {assigneeWarning(availability?.[value])}
        </Text>
      ) : null}
    </View>
  );
}

function Chip({ label, active, onPress, testID, warn = false }: { label: string; active: boolean; onPress: () => void; testID: string; warn?: boolean }) {
  return (
    <Pressable testID={testID} onPress={onPress} style={[styles.chip, warn ? styles.chipWarn : null, active ? styles.chipActive : null]}>
      <Text style={active ? styles.chipTextActive : styles.chipText} numberOfLines={1}>
        {label}
      </Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  label: { ...typography.body, color: colors.textMuted },
  row: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs, marginTop: spacing.xs },
  chip: {
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.lg,
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xs,
    maxWidth: 240,
  },
  chipWarn: { borderColor: colors.danger },
  warning: { ...typography.body, fontSize: 12, color: colors.danger, marginTop: spacing.xs },
  chipActive: { backgroundColor: colors.primaryMuted, borderColor: colors.primary },
  chipText: { ...typography.body, fontSize: 12, color: colors.textMuted },
  chipTextActive: { ...typography.body, fontSize: 12, color: colors.primary, fontWeight: '600' },
});
