import { useEffect, useState } from 'react';
import { ActivityIndicator, KeyboardAvoidingView, Modal, Platform, Pressable, StyleSheet, Text, TextInput, View } from 'react-native';
import { createTeam } from '../services/teamService';
import { colors, radius, spacing, typography } from '../theme/theme';
import type { ApiError } from '../types/api';
import type { TeamDto } from '../types/team';

type Props = {
  visible: boolean;
  onClose: () => void;
  onCreated: (team: TeamDto) => void;
};

export function CreateTeamModal({ visible, onClose, onCreated }: Props) {
  const [name, setName] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!visible) return;
    setName('');
    setError(null);
  }, [visible]);

  const submit = async () => {
    const trimmed = name.trim();
    if (!trimmed) return;
    setSubmitting(true);
    setError(null);
    try {
      const team = await createTeam({ name: trimmed, description: null });
      onCreated(team);
      onClose();
    } catch (err) {
      setError((err as ApiError).message ?? 'Failed to create team.');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal transparent animationType="fade" visible={visible} onRequestClose={onClose}>
      <KeyboardAvoidingView behavior={Platform.OS === 'ios' ? 'padding' : undefined} style={styles.flex}>
        <Pressable style={styles.backdrop} onPress={onClose}>
          <Pressable style={styles.sheet} onPress={() => undefined}>
            <Text style={styles.title}>New team</Text>
            <TextInput
              testID="create-team-modal-name-input"
              style={styles.input}
              placeholder="Team name"
              placeholderTextColor={colors.textMuted}
              value={name}
              onChangeText={setName}
              autoFocus
              onSubmitEditing={submit}
            />
            {error ? <Text style={styles.error}>{error}</Text> : null}
            <View style={styles.actions}>
              <Pressable
                testID="create-team-modal-submit"
                style={[styles.button, !name.trim() ? styles.buttonDisabled : null]}
                onPress={submit}
                disabled={submitting || !name.trim()}
              >
                {submitting ? <ActivityIndicator color={colors.surface} size="small" /> : <Text style={styles.buttonText}>Create</Text>}
              </Pressable>
              <Pressable testID="create-team-modal-cancel" onPress={onClose}>
                <Text style={styles.cancel}>Cancel</Text>
              </Pressable>
            </View>
          </Pressable>
        </Pressable>
      </KeyboardAvoidingView>
    </Modal>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  backdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.4)', justifyContent: 'flex-end' },
  sheet: {
    backgroundColor: colors.surface,
    borderTopLeftRadius: radius.md,
    borderTopRightRadius: radius.md,
    padding: spacing.md,
    gap: spacing.sm,
  },
  title: { ...typography.subtitle, color: colors.text },
  input: {
    ...typography.body,
    color: colors.text,
    borderWidth: 1,
    borderColor: colors.textMuted,
    borderRadius: radius.sm,
    padding: spacing.sm,
  },
  error: { ...typography.body, color: colors.danger },
  actions: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  button: { backgroundColor: colors.primary, borderRadius: radius.sm, paddingHorizontal: spacing.md, paddingVertical: spacing.sm },
  buttonDisabled: { opacity: 0.5 },
  buttonText: { color: colors.surface, fontWeight: '700' },
  cancel: { ...typography.body, color: colors.textMuted },
});
