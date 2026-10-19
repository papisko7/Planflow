import { useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Text, TextInput, View } from 'react-native';
import { useNavigation } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { requestPasswordReset, resetPassword } from '../services/authService';
import { colors, radius, spacing, typography } from '../theme/theme';
import type { AuthStackParamList } from '../navigation/types';
import type { ApiError } from '../types/api';

type Step = 'email' | 'code';

export default function ForgotPasswordScreen() {
  const navigation = useNavigation<NativeStackNavigationProp<AuthStackParamList>>();
  const [step, setStep] = useState<Step>('email');
  const [email, setEmail] = useState('');
  const [code, setCode] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  const sendCode = async () => {
    if (!email.trim()) {
      setError('Enter your e-mail address.');
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      await requestPasswordReset(email.trim());
      setInfo('If an account exists for this address, a 6-digit code is on its way. It is valid for 15 minutes.');
      setStep('code');
    } catch (err) {
      const apiError = err as ApiError;
      setError(apiError.status === 429 ? 'Too many attempts. Wait a minute and try again.' : (apiError.message ?? 'Could not send the code.'));
    } finally {
      setSubmitting(false);
    }
  };

  const submitReset = async () => {
    if (!/^\d{6}$/.test(code.trim())) {
      setError('The code has 6 digits.');
      return;
    }
    if (newPassword.length < 8) {
      setError('The new password needs at least 8 characters.');
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      await resetPassword({ email: email.trim(), code: code.trim(), newPassword });
      setDone(true);
    } catch (err) {
      const apiError = err as ApiError;
      if (apiError.status === 400) setError('The code is invalid or has expired. Request a new one.');
      else if (apiError.status === 429) setError('Too many attempts. Wait a minute and try again.');
      else setError(apiError.message ?? 'Could not reset the password.');
    } finally {
      setSubmitting(false);
    }
  };

  if (done) {
    return (
      <View style={styles.container}>
        <Text style={styles.title}>Password changed</Text>
        <Text style={styles.body}>You were signed out on every device. Sign in with your new password.</Text>
        <Pressable testID="forgot-back-to-login" style={styles.button} onPress={() => navigation.navigate('Login')}>
          <Text style={styles.buttonText}>Back to sign in</Text>
        </Pressable>
      </View>
    );
  }

  return (
    <View style={styles.container}>
      <Text style={styles.title}>Reset password</Text>

      {step === 'email' ? (
        <>
          <Text style={styles.body}>Enter the e-mail of your account and we will send you a code.</Text>
          <TextInput
            testID="forgot-email-input"
            style={styles.input}
            placeholder="Email"
            placeholderTextColor={colors.textMuted}
            autoCapitalize="none"
            autoCorrect={false}
            keyboardType="email-address"
            value={email}
            onChangeText={setEmail}
          />
          {error ? <Text style={styles.error}>{error}</Text> : null}
          <Pressable testID="forgot-send-code" style={styles.button} onPress={sendCode} disabled={submitting}>
            {submitting ? <ActivityIndicator color={colors.surface} /> : <Text style={styles.buttonText}>Send code</Text>}
          </Pressable>
        </>
      ) : (
        <>
          {info ? <Text style={styles.body}>{info}</Text> : null}
          <TextInput
            testID="forgot-code-input"
            style={styles.input}
            placeholder="6-digit code"
            placeholderTextColor={colors.textMuted}
            keyboardType="number-pad"
            maxLength={6}
            value={code}
            onChangeText={setCode}
          />
          <TextInput
            testID="forgot-password-input"
            style={styles.input}
            placeholder="New password (min. 8 characters)"
            placeholderTextColor={colors.textMuted}
            secureTextEntry
            autoCapitalize="none"
            value={newPassword}
            onChangeText={setNewPassword}
          />
          {error ? <Text style={styles.error}>{error}</Text> : null}
          <Pressable testID="forgot-submit" style={styles.button} onPress={submitReset} disabled={submitting}>
            {submitting ? <ActivityIndicator color={colors.surface} /> : <Text style={styles.buttonText}>Change password</Text>}
          </Pressable>
          <Pressable
            testID="forgot-resend"
            onPress={() => {
              setStep('email');
              setError(null);
              setCode('');
            }}
          >
            <Text style={styles.link}>Did not get a code? Send it again</Text>
          </Pressable>
        </>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background, padding: spacing.lg, justifyContent: 'center', gap: spacing.sm },
  title: { ...typography.title, color: colors.text, marginBottom: spacing.sm },
  body: { ...typography.body, color: colors.textMuted },
  input: {
    backgroundColor: colors.surface,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.md,
    padding: spacing.md,
    color: colors.text,
  },
  button: {
    backgroundColor: colors.primary,
    borderRadius: radius.md,
    padding: spacing.md,
    alignItems: 'center',
    marginTop: spacing.sm,
  },
  buttonText: { color: colors.surface, fontWeight: '700' },
  error: { color: colors.danger, ...typography.body },
  link: { color: colors.primary, textAlign: 'center', marginTop: spacing.md },
});
