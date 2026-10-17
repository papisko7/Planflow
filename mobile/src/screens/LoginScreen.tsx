import { useRef, useState } from 'react';
import {
  ActivityIndicator,
  Keyboard,
  Pressable,
  StyleSheet,
  Text,
  TextInput,
  TouchableWithoutFeedback,
  View,
} from 'react-native';
import { useNavigation } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { identifyByEmail, login, refreshAsUser } from '../services/authService';
import { isPinEnabledForUser, verifyPinForUser, clearPinForUser, isAnyPinEnabled } from '../services/pinService';
import { PinEntryModal } from '../components/PinEntryModal';
import { useSession } from '../context/SessionContext';
import { colors, radius, spacing, typography } from '../theme/theme';
import type { AuthStackParamList } from '../navigation/types';
import type { ApiError } from '../types/api';

type LoginStep = 'email' | 'auth';

const MAX_PASSWORD_FAILURES = 3;

export default function LoginScreen() {
  const navigation = useNavigation<NativeStackNavigationProp<AuthStackParamList>>();
  const { signIn } = useSession();
  const [step, setStep] = useState<LoginStep>('email');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [identifiedUserId, setIdentifiedUserId] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showPin, setShowPin] = useState(false);
  const [pinModalVisible, setPinModalVisible] = useState(false);
  const [pinError, setPinError] = useState<string | null>(null);
  const [passwordFailures, setPasswordFailures] = useState(0);
  const passwordRef = useRef<TextInput>(null);

  const showForgotHint = passwordFailures >= MAX_PASSWORD_FAILURES;

  // Step 1: look up the email on the server to get the userId, then check if that user has a PIN.
  const onIdentify = async () => {
    if (!email.trim()) {
      setError('Please enter your email.');
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      const { userId } = await identifyByEmail(email.trim());
      const pinEnabled = await isPinEnabledForUser(userId);
      setIdentifiedUserId(userId);
      setShowPin(pinEnabled);
      setStep('auth');
    } catch (err) {
      const apiErr = err as ApiError;
      if (apiErr.status === 404) {
        setError('No account found with this email.');
      } else if (apiErr.isNetworkError) {
        setError('No connection. Check your internet and try again.');
      } else {
        setError('Something went wrong. Please try again.');
      }
    } finally {
      setSubmitting(false);
    }
  };

  const onBack = () => {
    setStep('email');
    setPassword('');
    setIdentifiedUserId(null);
    setShowPin(false);
    setError(null);
    setPasswordFailures(0);
  };

  // Step 2: password login
  const onSubmit = async () => {
    if (!password) {
      setError('Please enter your password.');
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      await login({ email: email.trim(), password, teamId: null });
      signIn();
    } catch (err) {
      const apiErr = err as ApiError;
      let msg: string;
      if (apiErr.isNetworkError) {
        msg = 'No connection. Check your internet and try again.';
      } else if (apiErr.status === 400 || apiErr.status === 401) {
        msg = 'Incorrect password.';
      } else if (apiErr.status === 429) {
        msg = 'Too many attempts. Please wait before trying again.';
      } else {
        msg = 'Login failed. Please try again.';
      }
      setError(msg);
      setPasswordFailures((prev) => prev + 1);
    } finally {
      setSubmitting(false);
    }
  };

  // Step 2: PIN login — verifies against the already-identified user, so two users with the
  // same PIN never collide (only this user's PIN slot is checked).
  const onPinLogin = async (pin: string) => {
    if (!identifiedUserId) return;
    const match = await verifyPinForUser(identifiedUserId, pin);
    if (!match) {
      setPinError('Incorrect PIN. Try again.');
      return;
    }
    setPinError(null);
    setPinModalVisible(false);
    setSubmitting(true);
    try {
      await refreshAsUser(identifiedUserId, null);
      signIn();
    } catch (err) {
      const apiErr = err as ApiError;
      if (apiErr.status === 401) {
        await clearPinForUser(identifiedUserId);
        const anyLeft = await isAnyPinEnabled();
        setShowPin(anyLeft ? await isPinEnabledForUser(identifiedUserId) : false);
        setError('Your session has expired. Please sign in with your password.');
      } else {
        setError('Sign in with your password once to restore PIN login.');
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <TouchableWithoutFeedback onPress={Keyboard.dismiss} accessible={false}>
      <View style={styles.container}>
        <Text style={styles.title}>Sign in</Text>

        {step === 'email' ? (
          <>
            <TextInput
              testID="login-email-input"
              style={styles.input}
              placeholder="Email"
              placeholderTextColor={colors.textMuted}
              autoCapitalize="none"
              keyboardType="email-address"
              value={email}
              onChangeText={setEmail}
              returnKeyType="next"
              onSubmitEditing={onIdentify}
            />

            {error ? <Text style={styles.error}>{error}</Text> : null}

            <Pressable testID="login-continue-button" style={styles.button} onPress={onIdentify} disabled={submitting}>
              {submitting ? <ActivityIndicator color={colors.surface} /> : <Text style={styles.buttonText}>Continue</Text>}
            </Pressable>

            <Pressable testID="login-go-register-link" onPress={() => navigation.navigate('Register')}>
              <Text style={styles.link}>No account? Register</Text>
            </Pressable>
          </>
        ) : (
          <>
            <Pressable testID="login-back-button" style={styles.backButton} onPress={onBack}>
              <Text style={styles.backText}>← {email.trim()}</Text>
            </Pressable>

            <TextInput
              ref={passwordRef}
              testID="login-password-input"
              style={styles.input}
              placeholder="Password"
              placeholderTextColor={colors.textMuted}
              secureTextEntry
              value={password}
              onChangeText={setPassword}
              returnKeyType="done"
              onSubmitEditing={onSubmit}
              autoFocus
            />

            {error ? <Text style={styles.error}>{error}</Text> : null}

            <Pressable testID="login-submit-button" style={styles.button} onPress={onSubmit} disabled={submitting}>
              {submitting ? <ActivityIndicator color={colors.surface} /> : <Text style={styles.buttonText}>Sign in</Text>}
            </Pressable>

            {showPin ? (
              <Pressable
                testID="login-pin-button"
                style={styles.altButton}
                onPress={() => { setPinError(null); setPinModalVisible(true); }}
                disabled={submitting}
              >
                <Text style={styles.altButtonText}>Sign in with PIN</Text>
              </Pressable>
            ) : null}

            <PinEntryModal
              visible={pinModalVisible}
              title="Enter your PIN"
              error={pinError}
              onComplete={onPinLogin}
              onCancel={() => setPinModalVisible(false)}
            />

            <Pressable testID="login-go-forgot-link" onPress={() => navigation.navigate('ForgotPassword')}>
              <Text style={showForgotHint ? styles.hintLink : styles.link}>
                {showForgotHint ? 'Having trouble? Reset your password here.' : 'Forgot password?'}
              </Text>
            </Pressable>
          </>
        )}
      </View>
    </TouchableWithoutFeedback>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background, padding: spacing.lg, justifyContent: 'center', gap: spacing.sm },
  title: { ...typography.title, color: colors.text, marginBottom: spacing.md },
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
  altButton: {
    borderWidth: 1,
    borderColor: colors.primary,
    borderRadius: radius.md,
    padding: spacing.md,
    alignItems: 'center',
  },
  altButtonText: { color: colors.primary, fontWeight: '600' },
  error: { color: colors.danger, ...typography.body },
  link: { color: colors.primary, textAlign: 'center', marginTop: spacing.md },
  hintLink: { color: colors.danger, textAlign: 'center', marginTop: spacing.md, fontWeight: '600' },
  backButton: { paddingVertical: spacing.xs },
  backText: { color: colors.primary, fontWeight: '600' },
});
