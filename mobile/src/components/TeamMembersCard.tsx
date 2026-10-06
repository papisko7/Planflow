import { useCallback, useEffect, useState } from 'react';
import { ActivityIndicator, Alert, Pressable, StyleSheet, Text, TextInput, View } from 'react-native';
import { getCurrentUserId } from '../services/authService';
import { addTeamMember, changeMemberRole, getTeamMembers, removeTeamMember } from '../services/teamService';
import { colors, radius, spacing, typography } from '../theme/theme';
import { TeamRole, type TeamMemberDto } from '../types/team';
import type { ApiError } from '../types/api';

type Props = {
  teamId: string;
  // Lets the parent refresh its own "N members" counter after an add/remove/leave.
  onChanged: () => void;
};

const ROLE_LABEL: Record<TeamRole, string> = {
  [TeamRole.Owner]: 'Owner',
  [TeamRole.Admin]: 'Admin',
  [TeamRole.Member]: 'Member',
};

// Mirrors the backend rules (AddTeamMember/RemoveTeamMember handlers); the server stays the authority,
// this only decides which buttons are worth showing.
function canRemove(me: TeamMemberDto | undefined, target: TeamMemberDto): boolean {
  if (!me || target.role === TeamRole.Owner) return false;
  if (me.userId === target.userId) return true; // leave the team
  const canManage = me.role === TeamRole.Owner || me.role === TeamRole.Admin;
  return canManage && (target.role !== TeamRole.Admin || me.role === TeamRole.Owner);
}

// Only the owner hands out or takes away Admin (the server enforces it too); the owner's own role never changes.
function canChangeRole(me: TeamMemberDto | undefined, target: TeamMemberDto): boolean {
  return me?.role === TeamRole.Owner && target.role !== TeamRole.Owner && me.userId !== target.userId;
}

function describeError(err: unknown): string {
  const apiError = err as ApiError;
  switch (apiError.status) {
    case 404:
      return 'No PlanFlow user is registered with that e-mail.';
    case 409:
      return 'This user is already in the team.';
    case 403:
      return "You don't have permission to do that.";
    case 400:
      return 'Enter a valid e-mail address.';
    default:
      return apiError.message ?? 'Something went wrong.';
  }
}

export function TeamMembersCard({ teamId, onChanged }: Props) {
  const [members, setMembers] = useState<TeamMemberDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [email, setEmail] = useState('');
  const [role, setRole] = useState<TeamRole>(TeamRole.Member);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const myUserId = getCurrentUserId();
  const me = members.find((m) => m.userId === myUserId);
  const canManage = me?.role === TeamRole.Owner || me?.role === TeamRole.Admin;
  // Only the owner can appoint admins.
  const assignableRoles = me?.role === TeamRole.Owner ? [TeamRole.Member, TeamRole.Admin] : [TeamRole.Member];

  const load = useCallback(async () => {
    try {
      setMembers(await getTeamMembers(teamId));
      setError(null);
    } catch (err) {
      setError(describeError(err));
    } finally {
      setLoading(false);
    }
  }, [teamId]);

  useEffect(() => {
    setLoading(true);
    setMembers([]);
    load();
  }, [load]);

  const add = useCallback(async () => {
    const trimmed = email.trim();
    if (!trimmed) return;
    setBusy(true);
    setError(null);
    try {
      await addTeamMember(teamId, { email: trimmed, role });
      setEmail('');
      await load();
      onChanged();
    } catch (err) {
      setError(describeError(err));
    } finally {
      setBusy(false);
    }
  }, [email, role, teamId, load, onChanged]);

  const toggleRole = useCallback(
    async (target: TeamMemberDto) => {
      setBusy(true);
      setError(null);
      try {
        await changeMemberRole(teamId, target.userId, target.role === TeamRole.Admin ? TeamRole.Member : TeamRole.Admin);
        await load();
      } catch (err) {
        setError(describeError(err));
      } finally {
        setBusy(false);
      }
    },
    [teamId, load],
  );

  const remove = useCallback(
    (target: TeamMemberDto) => {
      const leaving = target.userId === myUserId;
      Alert.alert(
        leaving ? 'Leave team?' : `Remove ${target.userDisplayName}?`,
        leaving
          ? 'You will lose access to this team and its tasks.'
          : 'Their tasks in this team will become unassigned.',
        [
          { text: 'Cancel', style: 'cancel' },
          {
            text: leaving ? 'Leave' : 'Remove',
            style: 'destructive',
            onPress: async () => {
              setBusy(true);
              setError(null);
              try {
                await removeTeamMember(teamId, target.userId);
                await load();
                onChanged();
              } catch (err) {
                setError(describeError(err));
              } finally {
                setBusy(false);
              }
            },
          },
        ],
      );
    },
    [myUserId, teamId, load, onChanged],
  );

  return (
    <View style={styles.card}>
      <Text style={styles.label}>Members</Text>
      {loading ? (
        <ActivityIndicator color={colors.primary} />
      ) : (
        members.map((member) => (
          <View key={member.id} style={styles.row} testID={`member-row-${member.userId}`}>
            <View style={styles.rowText}>
              <Text style={styles.name}>
                {member.userDisplayName}
                {member.userId === myUserId ? ' (you)' : ''}
              </Text>
              <Text style={styles.role}>{ROLE_LABEL[member.role]}</Text>
            </View>
            {canChangeRole(me, member) ? (
              <Pressable testID={`change-role-${member.userId}`} onPress={() => toggleRole(member)} disabled={busy} style={styles.roleAction}>
                <Text style={styles.roleActionText}>{member.role === TeamRole.Admin ? 'Make member' : 'Make admin'}</Text>
              </Pressable>
            ) : null}
            {canRemove(me, member) ? (
              <Pressable testID={`remove-member-${member.userId}`} onPress={() => remove(member)} disabled={busy}>
                <Text style={styles.remove}>{member.userId === myUserId ? 'Leave' : 'Remove'}</Text>
              </Pressable>
            ) : null}
          </View>
        ))
      )}

      {canManage ? (
        <View style={styles.addForm}>
          <TextInput
            testID="add-member-email-input"
            style={styles.input}
            placeholder="Member's e-mail"
            placeholderTextColor={colors.textMuted}
            autoCapitalize="none"
            autoCorrect={false}
            keyboardType="email-address"
            value={email}
            onChangeText={setEmail}
          />
          <View style={styles.addActions}>
            {assignableRoles.map((option) => (
              <Pressable
                key={option}
                testID={`role-chip-${ROLE_LABEL[option]}`}
                onPress={() => setRole(option)}
                style={[styles.chip, role === option ? styles.chipActive : null]}
              >
                <Text style={[styles.chipText, role === option ? styles.chipTextActive : null]}>{ROLE_LABEL[option]}</Text>
              </Pressable>
            ))}
            <Pressable testID="add-member-submit" style={styles.addButton} onPress={add} disabled={busy || !email.trim()}>
              {busy ? <ActivityIndicator color={colors.surface} size="small" /> : <Text style={styles.addButtonText}>Add</Text>}
            </Pressable>
          </View>
        </View>
      ) : null}

      {error ? <Text style={styles.error}>{error}</Text> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  card: {
    backgroundColor: colors.surface,
    borderRadius: radius.md,
    padding: spacing.md,
    marginTop: spacing.md,
    gap: spacing.xs,
  },
  label: { ...typography.subtitle, color: colors.text, marginBottom: spacing.sm },
  row: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingVertical: spacing.xs,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: colors.border,
  },
  rowText: { flex: 1 },
  name: { ...typography.body, color: colors.text, fontWeight: '600' },
  role: { ...typography.body, fontSize: 12, color: colors.textMuted },
  remove: { ...typography.body, color: colors.danger, fontWeight: '600' },
  roleAction: { marginRight: spacing.md },
  roleActionText: { ...typography.body, color: colors.primary, fontWeight: '600' },
  addForm: { gap: spacing.sm, marginTop: spacing.sm },
  addActions: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs, flexWrap: 'wrap' },
  input: {
    backgroundColor: colors.background,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.md,
    padding: spacing.md,
    color: colors.text,
  },
  chip: {
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.sm,
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xs,
  },
  chipActive: { borderColor: colors.primary, backgroundColor: colors.primaryMuted },
  chipText: { ...typography.body, fontSize: 12, color: colors.textMuted },
  chipTextActive: { color: colors.primary, fontWeight: '700' },
  addButton: {
    backgroundColor: colors.primary,
    borderRadius: radius.sm,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.xs,
    marginLeft: 'auto',
  },
  addButtonText: { color: colors.surface, fontWeight: '700', fontSize: 12 },
  error: { ...typography.body, fontSize: 12, color: colors.danger },
});
