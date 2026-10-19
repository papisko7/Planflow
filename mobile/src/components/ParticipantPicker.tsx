import { Pressable, StyleSheet, Text, View } from 'react-native';
import { getCurrentUserId } from '../services/authService';
import { colors, radius, spacing, typography } from '../theme/theme';
import { LoadLevel, MemberStatus, type MemberAvailabilityDto } from '../types/availability';
import type { TeamMemberDto } from '../types/team';
import { assigneeWarning, pickerHint } from '../utils/availability';

type Props = {
  members: TeamMemberDto[];
  assigneeId: string | null;
  value: string[];
  onChange: (next: string[]) => void;
  label: string;
  availability?: Record<string, MemberAvailabilityDto>;
};

export function ParticipantPicker({ members, assigneeId, value, onChange, label, availability }: Props) {
  const myUserId = getCurrentUserId();
  const candidates = members.filter((m) => m.userId !== assigneeId);
  if (candidates.length === 0) return null;

  const toggle = (userId: string) => onChange(value.includes(userId) ? value.filter((id) => id !== userId) : [...value, userId]);

  return (
    <View testID="participant-picker">
      <Text style={styles.label}>{`${label} (optional):`}</Text>
      <View style={styles.row}>
        {candidates.map((m) => {
          const active = value.includes(m.userId);
          const info = availability?.[m.userId];
          const hint = pickerHint(info);
          const name = m.userId === myUserId ? `${m.userDisplayName} (you)` : m.userDisplayName;
          const warn = info?.currentStatus === MemberStatus.OutOfOffice || info?.loadLevel === LoadLevel.Overloaded;
          return (
            <Pressable
              key={m.userId}
              testID={`participant-chip-${m.userId}`}
              onPress={() => toggle(m.userId)}
              style={[styles.chip, warn ? styles.chipWarn : null, active ? styles.chipActive : null]}
            >
              <Text style={active ? styles.chipTextActive : styles.chipText} numberOfLines={1}>
                {hint ? `${name} · ${hint}` : name}
              </Text>
            </Pressable>
          );
        })}
      </View>
      {candidates
        .filter((m) => value.includes(m.userId) && assigneeWarning(availability?.[m.userId]))
        .map((m) => (
          <Text key={m.userId} testID={`participant-warning-${m.userId}`} style={styles.warning}>
            {assigneeWarning(availability?.[m.userId])}
          </Text>
        ))}
    </View>
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
