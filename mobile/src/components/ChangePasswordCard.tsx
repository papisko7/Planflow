import { useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Text, TextInput, View } from 'react-native';
import { changePassword } from '../services/userService';
import { colors, radius, spacing, typography } from '../theme/theme';
import type { ApiError } from '../types/api';

// Change the password while signed in: the current password proves it is really the owner holding the phone, and the
// server signs every OTHER device out (this one stays signed in).
export function ChangePasswordCard() {
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [repeat, setRepeat] = useState('');
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ text: string; isError: boolean } | null>(null);

  const submit = async () => {
    if (!current) {
      setMessage({ text: 'Enter your current password.', isError: true });
      return;
    }
    if (next.length < 8) {
      setMessage({ text: 'The new password needs at least 8 characters.', isError: true });
      return;
    }
    if (next === current) {
      setMessage({ text: 'The new password must differ from the current one.', isError: true });
      return;
    }
    if (next !== repeat) {
      setMessage({ text: 'The two new passwords do not match.', isError: true });
      return;
    }
    setBusy(true);
    setMessage(null);
    try {
      await changePassword(current, next);
      setCurrent('');
      setNext('');
      setRepeat('');
      setMessage({ text: 'Password changed. Your other devices were signed out.', isError: false });
    } catch (err) {
      const apiError = err as ApiError;
      let text = apiError.message ?? 'Could not change the password.';
      if (apiError.status === 400) text = 'The current password is incorrect.';
      else if (apiError.status === 429) text = 'Too many attempts. Wait a minute and try again.';
      setMessage({ text, isError: true });
    } finally {
      setBusy(false);
    }
  };

  return (
    <View testID="change-password-card" style={styles.card}>
      <Text style={styles.sectionTitle}>Change password</Text>
      <TextInput
        testID="password-current-input"
        style={styles.input}
        placeholder="Current password"
        placeholderTextColor={colors.textMuted}
        secureTextEntry
        autoCapitalize="none"
        value={current}
        onChangeText={setCurrent}
      />
      <TextInput
        testID="password-new-input"
        style={styles.input}
        placeholder="New password (min. 8 characters)"
        placeholderTextColor={colors.textMuted}
        secureTextEntry
        autoCapitalize="none"
        value={next}
        onChangeText={setNext}
      />
      <TextInput
        testID="password-repeat-input"
        style={styles.input}
        placeholder="Repeat the new password"
        placeholderTextColor={colors.textMuted}
        secureTextEntry
        autoCapitalize="none"
        value={repeat}
        onChangeText={setRepeat}
      />
      {message ? <Text style={message.isError ? styles.error : styles.success}>{message.text}</Text> : null}
      <Pressable testID="password-submit" style={styles.button} onPress={submit} disabled={busy}>
        {busy ? <ActivityIndicator color={colors.surface} size="small" /> : <Text style={styles.buttonText}>Change password</Text>}
      </Pressable>
    </View>
  );
}

const styles = StyleSheet.create({
  card: { backgroundColor: colors.surface, borderRadius: radius.md, padding: spacing.md, gap: spacing.xs },
  sectionTitle: { ...typography.subtitle, color: colors.text, marginBottom: spacing.xs },
  input: {
    backgroundColor: colors.background,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.md,
    padding: spacing.md,
    color: colors.text,
  },
  error: { ...typography.body, color: colors.danger },
  success: { ...typography.body, color: colors.primary, fontWeight: '600' },
  button: { backgroundColor: colors.primary, borderRadius: radius.md, padding: spacing.md, alignItems: 'center', marginTop: spacing.sm },
  buttonText: { color: colors.surface, fontWeight: '700' },
});
