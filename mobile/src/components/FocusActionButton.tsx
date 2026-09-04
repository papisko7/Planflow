import { Pressable, StyleSheet, Text } from 'react-native';
import { colors, radius, spacing, typography } from '../theme/theme';

interface FocusActionButtonProps {
  label: string;
  onPress: () => void;
  disabled?: boolean;
  testID?: string;
}

export function PrimaryButton({ label, onPress, disabled, testID }: FocusActionButtonProps) {
  return (
    <Pressable
      testID={testID}
      onPress={onPress}
      disabled={disabled}
      style={({ pressed }) => [styles.base, styles.primary, { opacity: pressed || disabled ? 0.7 : 1 }]}
    >
      <Text style={styles.primaryText}>{label}</Text>
    </Pressable>
  );
}

export function SecondaryButton({ label, onPress, disabled, testID }: FocusActionButtonProps) {
  return (
    <Pressable
      testID={testID}
      onPress={onPress}
      disabled={disabled}
      style={({ pressed }) => [styles.base, styles.secondary, { opacity: pressed || disabled ? 0.7 : 1 }]}
    >
      <Text style={styles.secondaryText}>{label}</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  base: { borderRadius: radius.md, paddingVertical: spacing.md, alignItems: 'center' },
  primary: { backgroundColor: colors.primary },
  primaryText: { ...typography.subtitle, color: colors.surface },
  secondary: { backgroundColor: colors.surface, borderWidth: 1, borderColor: colors.border },
  secondaryText: { ...typography.subtitle, color: colors.text },
});
