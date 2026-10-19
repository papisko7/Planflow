import { useEffect, useState } from 'react';
import { ActivityIndicator, KeyboardAvoidingView, Modal, Platform, Pressable, StyleSheet, Text, TextInput, View } from 'react-native';
import { createList, renameList } from '../services/listService';
import { colors, radius, spacing, typography } from '../theme/theme';
import { ItemListKind, type ItemListDto } from '../types/itemList';
import type { ApiError } from '../types/api';
import type { TeamDto } from '../types/team';

type Props = {
  visible: boolean;
  teams: TeamDto[];
  list?: ItemListDto | null;
  defaultKind?: ItemListKind;
  defaultTeamId?: string | null;
  onClose: () => void;
  onSaved: (list: ItemListDto) => void;
};

const KIND_LABEL: Record<ItemListKind, string> = {
  [ItemListKind.TaskList]: 'Task list',
  [ItemListKind.MeetingList]: 'Meeting list',
};

export function ListFormModal({ visible, teams, list = null, defaultKind = ItemListKind.TaskList, defaultTeamId, onClose, onSaved }: Props) {
  const renaming = !!list;
  const [name, setName] = useState('');
  const [kind, setKind] = useState(defaultKind);
  const [teamId, setTeamId] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!visible) return;
    setName(list?.name ?? '');
    setKind(list?.kind ?? defaultKind);
    setTeamId(list?.teamId ?? (teams.find((t) => t.id === defaultTeamId) ?? teams[0])?.id ?? null);
    setError(null);
    // Only reset on open; later changes to the props must not wipe what the user is typing.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [visible]);

  const submit = async () => {
    const trimmed = name.trim();
    if (!trimmed) {
      setError('Name is required.');
      return;
    }
    if (!teamId) {
      setError('Pick a team first.');
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      const saved = list ? await renameList(list.teamId, list.id, trimmed) : await createList(teamId, trimmed, kind);
      onSaved(saved);
      onClose();
    } catch (err) {
      const apiError = err as ApiError;
      setError(
        apiError.status === 403
          ? 'Only the person who made the list, or a team owner or admin, can change it.'
          : (apiError.message ?? 'Could not save the list.'),
      );
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal transparent animationType="slide" visible={visible} onRequestClose={onClose}>
      <KeyboardAvoidingView style={styles.backdrop} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <Pressable style={styles.dismissArea} onPress={onClose} />
        <View style={styles.sheet}>
          <Text style={styles.heading}>{renaming ? 'Rename list' : 'New list'}</Text>
          <TextInput
            testID="list-name-input"
            style={styles.input}
            placeholder="List name"
            placeholderTextColor={colors.textMuted}
            value={name}
            onChangeText={setName}
            maxLength={100}
            autoFocus
          />

          {renaming ? null : (
            <>
              <Text style={styles.label}>Holds:</Text>
              <View style={styles.chipRow}>
                {[ItemListKind.TaskList, ItemListKind.MeetingList].map((k) => (
                  <Pressable
                    key={k}
                    testID={`list-kind-${k}`}
                    onPress={() => setKind(k)}
                    style={[styles.chip, kind === k ? styles.chipActive : null]}
                  >
                    <Text style={kind === k ? styles.chipTextActive : styles.chipText}>{KIND_LABEL[k]}</Text>
                  </Pressable>
                ))}
              </View>
              {teams.length > 1 ? (
                <>
                  <Text style={styles.label}>Team:</Text>
                  <View style={styles.chipRow}>
                    {teams.map((t) => (
                      <Pressable
                        key={t.id}
                        testID={`list-team-${t.id}`}
                        onPress={() => setTeamId(t.id)}
                        style={[styles.chip, teamId === t.id ? styles.chipActive : null]}
                      >
                        <Text style={teamId === t.id ? styles.chipTextActive : styles.chipText} numberOfLines={1}>
                          {t.isPersonal ? 'Personal' : t.name}
                        </Text>
                      </Pressable>
                    ))}
                  </View>
                </>
              ) : null}
            </>
          )}

          {error ? <Text style={styles.error}>{error}</Text> : null}

          <View style={styles.actions}>
            <Pressable testID="list-form-submit" style={styles.button} onPress={submit} disabled={submitting}>
              {submitting ? <ActivityIndicator color={colors.surface} size="small" /> : <Text style={styles.buttonText}>{renaming ? 'Save' : 'Create'}</Text>}
            </Pressable>
            <Pressable testID="list-form-cancel" onPress={onClose} disabled={submitting}>
              <Text style={styles.label}>Cancel</Text>
            </Pressable>
          </View>
        </View>
      </KeyboardAvoidingView>
    </Modal>
  );
}

const styles = StyleSheet.create({
  backdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.4)', justifyContent: 'flex-end' },
  dismissArea: { flex: 1 },
  sheet: {
    backgroundColor: colors.surface,
    borderTopLeftRadius: radius.lg,
    borderTopRightRadius: radius.lg,
    padding: spacing.lg,
    gap: spacing.sm,
  },
  heading: { ...typography.title, color: colors.text },
  input: {
    backgroundColor: colors.background,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.md,
    padding: spacing.md,
    color: colors.text,
  },
  label: { ...typography.body, color: colors.textMuted },
  error: { ...typography.body, color: colors.danger },
  chipRow: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs },
  chip: { borderWidth: 1, borderColor: colors.border, borderRadius: radius.lg, paddingHorizontal: spacing.sm, paddingVertical: spacing.xs, maxWidth: 160 },
  chipActive: { backgroundColor: colors.primaryMuted, borderColor: colors.primary },
  chipText: { ...typography.body, fontSize: 12, color: colors.textMuted },
  chipTextActive: { ...typography.body, fontSize: 12, color: colors.primary, fontWeight: '600' },
  actions: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  button: { backgroundColor: colors.primary, borderRadius: radius.sm, paddingHorizontal: spacing.lg, paddingVertical: spacing.sm },
  buttonText: { color: colors.surface, fontWeight: '700' },
});
