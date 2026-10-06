import { useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Text, TextInput, View } from 'react-native';
import { resendEmailVerification, verifyEmail } from '../services/userService';
import { colors, radius, spacing, typography } from '../theme/theme';
import type { ApiError } from '../types/api';

interface Props {
  email: string;
  onVerified: () => void;
}

// Shown while the account's e-mail address is not confirmed yet. The code arrives by e-mail (right after sign-up, or after
// the address was changed); until it is entered the app sends only account mails to this address, never task or team mail.
export function EmailVerificationCard({ email, onVerified }: Props) {
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ text: string; isError: boolean } | null>(null);

  const confirm = async () => {
    if (!/^\d{6}$/.test(code.trim())) {
      setMessage({ text: 'The code has 6 digits.', isError: true });
      return;
    }
    setBusy(true);
    setMessage(null);
    try {
      await verifyEmail(code.trim());
      onVerified();
    } catch (err) {
      const apiError = err as ApiError;
      if (apiError.status === 400) setMessage({ text: 'The code is invalid or has expired. Send a new one.', isError: true });
      else if (apiError.status === 429) setMessage({ text: 'Too many attempts. Wait a minute and try again.', isError: true });
      else setMessage({ text: apiError.message ?? 'Could not confirm the e-mail.', isError: true });
    } finally {
      setBusy(false);
    }
  };

  const resend = async () => {
    setMessage(null);
    try {
      await resendEmailVerification();
      setMessage({ text: 'If a code was not sent a moment ago, a new one is on its way.', isError: false });
    } catch (err) {
      const apiError = err as ApiError;
      setMessage({
        text: apiError.status === 429 ? 'Too many attempts. Wait a minute and try again.' : (apiError.message ?? 'Could not send the code.'),
        isError: true,
      });
    }
  };

  return (
    <View testID="email-verification-card" style={styles.card}>
      <Text style={styles.title}>Confirm your e-mail</Text>
      <Text style={styles.body}>
        We sent a 6-digit code to {email}. Until you enter it you will not get task or team e-mails.
      </Text>
      <TextInput
        testID="verify-code-input"
        style={styles.input}
        placeholder="6-digit code"
        placeholderTextColor={colors.textMuted}
        keyboardType="number-pad"
        maxLength={6}
        value={code}
        onChangeText={setCode}
      />
      {message ? <Text style={message.isError ? styles.error : styles.success}>{message.text}</Text> : null}
      <Pressable testID="verify-submit" style={styles.button} onPress={confirm} disabled={busy}>
        {busy ? <ActivityIndicator color={colors.surface} size="small" /> : <Text style={styles.buttonText}>Confirm</Text>}
      </Pressable>
      <Pressable testID="verify-resend" onPress={resend} hitSlop={8}>
        <Text style={styles.link}>Send a new code</Text>
      </Pressable>
    </View>
  );
}

const styles = StyleSheet.create({
  card: { backgroundColor: colors.surface, borderRadius: radius.md, padding: spacing.md, gap: spacing.xs, borderWidth: 1, borderColor: colors.primary },
  title: { ...typography.subtitle, color: colors.text },
  body: { ...typography.body, color: colors.textMuted },
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
  button: { backgroundColor: colors.primary, borderRadius: radius.md, padding: spacing.md, alignItems: 'center', marginTop: spacing.xs },
  buttonText: { color: colors.surface, fontWeight: '700' },
  link: { color: colors.primary, textAlign: 'center', marginTop: spacing.xs },
});
