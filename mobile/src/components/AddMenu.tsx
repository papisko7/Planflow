import { Modal, Pressable, StyleSheet, Text, View } from 'react-native';
import { colors, radius, spacing, typography } from '../theme/theme';

type Props = {
  visible: boolean;
  onClose: () => void;
  onNewTask: () => void;
  onNewTeam: () => void;
};

// What the Dashboard "+" opens: pick whether to add a task or a team. Close first, then run the choice,
// so the next sheet never opens underneath this one.
export function AddMenu({ visible, onClose, onNewTask, onNewTeam }: Props) {
  const run = (action: () => void) => () => {
    onClose();
    action();
  };

  return (
    <Modal transparent animationType="fade" visible={visible} onRequestClose={onClose}>
      <Pressable style={styles.backdrop} onPress={onClose}>
        <View style={styles.sheet}>
          <Pressable testID="add-menu-task" style={styles.item} onPress={run(onNewTask)}>
            <Text style={styles.itemText}>New item</Text>
          </Pressable>
          <Pressable testID="add-menu-team" style={styles.item} onPress={run(onNewTeam)}>
            <Text style={styles.itemText}>New team</Text>
          </Pressable>
          <Pressable testID="add-menu-cancel" style={styles.item} onPress={onClose}>
            <Text style={[styles.itemText, styles.muted]}>Cancel</Text>
          </Pressable>
        </View>
      </Pressable>
    </Modal>
  );
}

const styles = StyleSheet.create({
  backdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.4)', justifyContent: 'flex-end' },
  sheet: {
    backgroundColor: colors.surface,
    borderTopLeftRadius: radius.md,
    borderTopRightRadius: radius.md,
    padding: spacing.md,
    gap: spacing.xs,
  },
  item: { paddingVertical: spacing.md },
  itemText: { ...typography.body, color: colors.text, fontWeight: '600' },
  muted: { color: colors.textMuted },
});
