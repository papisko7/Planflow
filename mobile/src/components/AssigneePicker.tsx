import { Pressable, StyleSheet, Text, View } from 'react-native';
import { getCurrentUserId } from '../services/authService';
import { colors, radius, spacing, typography } from '../theme/theme';
import type { TeamMemberDto } from '../types/team';

type Props = {
  members: TeamMemberDto[];
  value: string | null;
  onChange: (userId: string | null) => void;
};

// Chip row for choosing who a task is assigned to. Only shown to Owner/Admin; the backend enforces it too.
export function AssigneePicker({ members, value, onChange }: Props) {
  const myUserId = getCurrentUserId();
  return (
    <View>
      <Text style={styles.label}>Assigned to:</Text>
      <View style={styles.row}>
        <Chip testID="assignee-chip-none" label="Unassigned" active={value === null} onPress={() => onChange(null)} />
        {members.map((m) => (
          <Chip
            key={m.userId}
            testID={`assignee-chip-${m.userId}`}
            label={m.userId === myUserId ? `${m.userDisplayName} (you)` : m.userDisplayName}
            active={value === m.userId}
            onPress={() => onChange(m.userId)}
          />
        ))}
      </View>
    </View>
  );
}

function Chip({ label, active, onPress, testID }: { label: string; active: boolean; onPress: () => void; testID: string }) {
  return (
    <Pressable testID={testID} onPress={onPress} style={[styles.chip, active ? styles.chipActive : null]}>
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
    maxWidth: 160,
  },
  chipActive: { backgroundColor: colors.primaryMuted, borderColor: colors.primary },
  chipText: { ...typography.body, fontSize: 12, color: colors.textMuted },
  chipTextActive: { ...typography.body, fontSize: 12, color: colors.primary, fontWeight: '600' },
});
