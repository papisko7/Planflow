import { Pressable, StyleSheet, Text } from 'react-native';
import { colors, spacing } from '../theme/theme';

type Props = { onPress: () => void; testID?: string; accessibilityLabel?: string };

export function Fab({ onPress, testID = 'fab-new-task', accessibilityLabel = 'New task' }: Props) {
  return (
    <Pressable
      testID={testID}
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel}
      onPress={onPress}
      style={({ pressed }) => [styles.fab, { opacity: pressed ? 0.85 : 1 }]}
    >
      <Text style={styles.plus}>+</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  fab: {
    position: 'absolute',
    right: spacing.lg,
    bottom: spacing.lg,
    width: 56,
    height: 56,
    borderRadius: 28,
    backgroundColor: colors.primary,
    alignItems: 'center',
    justifyContent: 'center',
    elevation: 6,
    shadowColor: '#000',
    shadowOpacity: 0.25,
    shadowRadius: 6,
    shadowOffset: { width: 0, height: 3 },
  },
  plus: { color: colors.surface, fontSize: 30, lineHeight: 34, fontWeight: '500' },
});
