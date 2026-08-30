import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useSession } from '../context/SessionContext';
import { colors, radius, spacing, typography } from '../theme/theme';

export default function SettingsScreen() {
  const { signOut } = useSession();

  return (
    <View style={styles.container}>
      <Text style={styles.title}>Settings</Text>
      <Text style={styles.subtitle}>Account, group, and calendar sync settings go here.</Text>

      <Pressable testID="settings-signout-button" style={styles.button} onPress={() => signOut()}>
        <Text style={styles.buttonText}>Sign out</Text>
      </Pressable>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: colors.background,
    padding: spacing.lg,
  },
  title: { ...typography.title, color: colors.text },
  subtitle: { ...typography.body, color: colors.textMuted, marginTop: spacing.sm },
  button: {
    backgroundColor: colors.danger,
    borderRadius: radius.md,
    padding: spacing.md,
    alignItems: 'center',
    marginTop: spacing.lg,
    alignSelf: 'flex-start',
    paddingHorizontal: spacing.lg,
  },
  buttonText: { color: colors.surface, fontWeight: '700' },
});
