import { useEffect, useState } from 'react';
import { ActivityIndicator, Alert, Pressable, StyleSheet, Text, TextInput, View } from 'react-native';
import { getCurrentUserId } from '../services/authService';
import { deleteTeam, getTeamMembers, updateTeam } from '../services/teamService';
import { colors, radius, spacing, typography } from '../theme/theme';
import { TeamRole, type TeamDto } from '../types/team';
import type { ApiError } from '../types/api';

type Props = {
  team: TeamDto;
  onChanged: () => void;
  onDeleted: () => void;
};

export function TeamSettingsCard({ team, onChanged, onDeleted }: Props) {
  const [role, setRole] = useState<TeamRole | null>(null);
  const [editing, setEditing] = useState(false);
  const [name, setName] = useState(team.name);
  const [description, setDescription] = useState(team.description ?? '');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setRole(null);
    setEditing(false);
    setError(null);
    getTeamMembers(team.id)
      .then((members) => {
        if (!cancelled) setRole(members.find((m) => m.userId === getCurrentUserId())?.role ?? null);
      })
      .catch(() => {
        if (!cancelled) setRole(null);
      });
    return () => {
      cancelled = true;
    };
  }, [team.id]);

  const canEdit = role === TeamRole.Owner || role === TeamRole.Admin;
  const canDelete = role === TeamRole.Owner;
  if (!canEdit) return null;

  const startEditing = () => {
    setName(team.name);
    setDescription(team.description ?? '');
    setError(null);
    setEditing(true);
  };

  const save = async () => {
    if (!name.trim()) {
      setError('Team name is required.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await updateTeam(team.id, { name: name.trim(), description: description.trim() || null });
      setEditing(false);
      onChanged();
    } catch (err) {
      const apiError = err as ApiError;
      setError(apiError.status === 403 ? "You don't have permission to edit this team." : (apiError.message ?? 'Failed to save.'));
    } finally {
      setBusy(false);
    }
  };

  const confirmDelete = () => {
    Alert.alert('Delete team?', `"${team.name}" with all its tasks and chat will be permanently removed for every member.`, [
      { text: 'Cancel', style: 'cancel' },
      {
        text: 'Delete',
        style: 'destructive',
        onPress: async () => {
          setBusy(true);
          setError(null);
          try {
            await deleteTeam(team.id);
            onDeleted();
          } catch (err) {
            const apiError = err as ApiError;
            setError(apiError.status === 403 ? 'Only the team owner can delete the team.' : (apiError.message ?? 'Failed to delete.'));
          } finally {
            setBusy(false);
          }
        },
      },
    ]);
  };

  return (
    <View style={styles.card}>
      {editing ? (
        <View style={styles.form}>
          <TextInput
            testID="edit-team-name-input"
            style={styles.input}
            placeholder="Team name"
            placeholderTextColor={colors.textMuted}
            value={name}
            onChangeText={setName}
          />
          <TextInput
            testID="edit-team-description-input"
            style={styles.input}
            placeholder="Description (optional)"
            placeholderTextColor={colors.textMuted}
            value={description}
            onChangeText={setDescription}
          />
          <View style={styles.actions}>
            <Pressable testID="edit-team-save" style={styles.primaryButton} onPress={save} disabled={busy}>
              {busy ? <ActivityIndicator color={colors.surface} size="small" /> : <Text style={styles.primaryText}>Save</Text>}
            </Pressable>
            <Pressable testID="edit-team-cancel" onPress={() => setEditing(false)} disabled={busy}>
              <Text style={styles.muted}>Cancel</Text>
            </Pressable>
          </View>
        </View>
      ) : (
        <View style={styles.actions}>
          <Pressable testID="edit-team-button" style={styles.primaryButton} onPress={startEditing}>
            <Text style={styles.primaryText}>Edit team</Text>
          </Pressable>
          {canDelete ? (
            <Pressable testID="delete-team-button" onPress={confirmDelete} disabled={busy}>
              <Text style={styles.danger}>Delete team</Text>
            </Pressable>
          ) : null}
        </View>
      )}
      {error ? <Text style={styles.danger}>{error}</Text> : null}
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
  form: { gap: spacing.sm },
  input: {
    backgroundColor: colors.background,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.md,
    padding: spacing.md,
    color: colors.text,
  },
  actions: { flexDirection: 'row', alignItems: 'center', gap: spacing.lg },
  primaryButton: {
    backgroundColor: colors.primary,
    borderRadius: radius.sm,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.xs,
  },
  primaryText: { color: colors.surface, fontWeight: '700', fontSize: 12 },
  muted: { ...typography.body, color: colors.textMuted },
  danger: { ...typography.body, color: colors.danger, fontWeight: '600' },
});
