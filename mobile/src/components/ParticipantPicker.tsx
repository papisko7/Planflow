import { Pressable, StyleSheet, Text, View } from 'react-native';
import { getCurrentUserId } from '../services/authService';
import { colors, radius, spacing, typography } from '../theme/theme';
import type { TeamMemberDto } from '../types/team';

type Props = {
  members: TeamMemberDto[];
  // The person in charge is not also a guest / co-contractor, so they are not offered.
  assigneeId: string | null;
  value: string[];
  onChange: (next: string[]) => void;
  // "Guests" for an event, "Co-contractors" for a task.
  label: string;
};

// Chip row for inviting team members to an item next to its assignee: guests of an event, co-contractors of a task.
// Tapping a chip toggles the person. The backend checks the same rules (team members only, at most ten).
export function ParticipantPicker({ members, assigneeId, value, onChange, label }: Props) {
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
          return (
            <Pressable
              key={m.userId}
              testID={`participant-chip-${m.userId}`}
              onPress={() => toggle(m.userId)}
              style={[styles.chip, active ? styles.chipActive : null]}
            >
              <Text style={active ? styles.chipTextActive : styles.chipText} numberOfLines={1}>
                {m.userId === myUserId ? `${m.userDisplayName} (you)` : m.userDisplayName}
              </Text>
            </Pressable>
          );
        })}
      </View>
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
    maxWidth: 160,
  },
  chipActive: { backgroundColor: colors.primaryMuted, borderColor: colors.primary },
  chipText: { ...typography.body, fontSize: 12, color: colors.textMuted },
  chipTextActive: { ...typography.body, fontSize: 12, color: colors.primary, fontWeight: '600' },
});
