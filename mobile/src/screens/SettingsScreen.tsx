import { useEffect, useState } from 'react';
import { ActivityIndicator, Pressable, ScrollView, StyleSheet, Text, TextInput, View } from 'react-native';
import { ChangePasswordCard } from '../components/ChangePasswordCard';
import { EmailVerificationCard } from '../components/EmailVerificationCard';
import { NotificationSettingsCard } from '../components/NotificationSettingsCard';
import { useSession } from '../context/SessionContext';
import { getProfile, updateProfile } from '../services/userService';
import { colors, radius, spacing, typography } from '../theme/theme';
import type { ApiError } from '../types/api';

export default function SettingsScreen() {
  const { signOut } = useSession();

  const [loading, setLoading] = useState(true);
  const [savedEmail, setSavedEmail] = useState('');
  const [emailVerified, setEmailVerified] = useState(true); // assume confirmed until the profile says otherwise
  const [displayName, setDisplayName] = useState('');
  const [email, setEmail] = useState('');
  const [currentPassword, setCurrentPassword] = useState('');
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<{ text: string; isError: boolean } | null>(null);

  useEffect(() => {
    getProfile()
      .then((profile) => {
        setDisplayName(profile.displayName);
        setEmail(profile.email);
        setSavedEmail(profile.email);
        setEmailVerified(profile.emailVerified);
      })
      .catch((err: ApiError) => setMessage({ text: err.message ?? 'Failed to load profile.', isError: true }))
      .finally(() => setLoading(false));
  }, []);

  // Changing the e-mail (the login) asks for the current password; changing only the name does not.
  const emailChanged = email.trim() !== savedEmail;

  const save = async () => {
    if (!displayName.trim() || !email.trim()) {
      setMessage({ text: 'Name and e-mail are required.', isError: true });
      return;
    }
    if (emailChanged && !currentPassword) {
      setMessage({ text: 'Enter your current password to change the e-mail.', isError: true });
      return;
    }
    setSaving(true);
    setMessage(null);
    try {
      const profile = await updateProfile({
        displayName: displayName.trim(),
        email: email.trim(),
        currentPassword: emailChanged ? currentPassword : null,
      });
      setDisplayName(profile.displayName);
      setEmail(profile.email);
      setSavedEmail(profile.email);
      setEmailVerified(profile.emailVerified);
      setCurrentPassword('');
      setMessage({
        text: profile.emailVerified ? 'Profile saved.' : 'Profile saved. We sent a code to the new address — confirm it below.',
        isError: false,
      });
    } catch (err) {
      const apiError = err as ApiError;
      let text = apiError.message ?? 'Failed to save profile.';
      if (apiError.status === 409) text = 'An account with this e-mail already exists.';
      else if (apiError.status === 400) text = 'Check the e-mail format and your current password.';
      setMessage({ text, isError: true });
    } finally {
      setSaving(false);
    }
  };

  return (
    <ScrollView style={styles.container} contentContainerStyle={styles.content}>
      <Text style={styles.title}>Settings</Text>

      <View style={styles.card}>
        <Text style={styles.sectionTitle}>Profile</Text>
        {loading ? (
          <ActivityIndicator color={colors.primary} />
        ) : (
          <>
            <Text style={styles.label}>Name</Text>
            <TextInput
              testID="profile-name-input"
              style={styles.input}
              placeholderTextColor={colors.textMuted}
              value={displayName}
              onChangeText={setDisplayName}
            />
            <Text style={styles.label}>E-mail (used to log in)</Text>
            <TextInput
              testID="profile-email-input"
              style={styles.input}
              placeholderTextColor={colors.textMuted}
              autoCapitalize="none"
              autoCorrect={false}
              keyboardType="email-address"
              value={email}
              onChangeText={setEmail}
            />
            {emailChanged ? (
              <>
                <Text style={styles.label}>Current password</Text>
                <TextInput
                  testID="profile-password-input"
                  style={styles.input}
                  placeholderTextColor={colors.textMuted}
                  secureTextEntry
                  autoCapitalize="none"
                  value={currentPassword}
                  onChangeText={setCurrentPassword}
                />
              </>
            ) : null}
            {message ? <Text style={message.isError ? styles.error : styles.success}>{message.text}</Text> : null}
            <Pressable testID="profile-save-button" style={styles.saveButton} onPress={save} disabled={saving}>
              {saving ? <ActivityIndicator color={colors.surface} size="small" /> : <Text style={styles.buttonText}>Save profile</Text>}
            </Pressable>
          </>
        )}
      </View>

      {!loading && !emailVerified ? <EmailVerificationCard email={savedEmail} onVerified={() => setEmailVerified(true)} /> : null}

      <ChangePasswordCard />

      <NotificationSettingsCard />

      <Pressable testID="settings-signout-button" style={styles.signOutButton} onPress={() => signOut()}>
        <Text style={styles.buttonText}>Sign out</Text>
      </Pressable>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  content: { padding: spacing.lg, gap: spacing.md },
  title: { ...typography.title, color: colors.text },
  card: { backgroundColor: colors.surface, borderRadius: radius.md, padding: spacing.md, gap: spacing.xs },
  sectionTitle: { ...typography.subtitle, color: colors.text, marginBottom: spacing.xs },
  label: { ...typography.body, fontSize: 12, color: colors.textMuted, marginTop: spacing.xs },
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
  saveButton: {
    backgroundColor: colors.primary,
    borderRadius: radius.md,
    padding: spacing.md,
    alignItems: 'center',
    marginTop: spacing.sm,
  },
  signOutButton: {
    backgroundColor: colors.danger,
    borderRadius: radius.md,
    padding: spacing.md,
    alignItems: 'center',
    alignSelf: 'flex-start',
    paddingHorizontal: spacing.lg,
  },
  buttonText: { color: colors.surface, fontWeight: '700' },
});
