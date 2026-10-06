import { Modal, Pressable, StyleSheet, Text, View } from 'react-native';
import { colors, radius, spacing, typography } from '../theme/theme';

type Props = {
  visible: boolean;
  title: string;
  onClose: () => void;
  onOpen: () => void;
  onClaim?: () => void;
  onAbandon?: () => void;
  onEdit?: () => void;
  onComplete?: () => void;
  onReopen?: () => void;
  onDelete?: () => void;
};

// Bottom sheet shown on long-press of a task: every quick action in one place, no extra screen.
export function TaskActionMenu({ visible, title, onClose, onOpen, onClaim, onAbandon, onEdit, onComplete, onReopen, onDelete }: Props) {
  // Close first, then run the action, so an Alert/navigation never opens underneath the sheet.
  const run = (action?: () => void) => () => {
    onClose();
    action?.();
  };

  return (
    <Modal transparent animationType="fade" visible={visible} onRequestClose={onClose}>
      <Pressable style={styles.backdrop} onPress={onClose}>
        <View style={styles.sheet}>
          <Text style={styles.title} numberOfLines={1}>
            {title}
          </Text>
          <MenuItem testID="task-menu-open" label="Open details" onPress={run(onOpen)} />
          {onClaim ? <MenuItem testID="task-menu-claim" label="Take over" onPress={run(onClaim)} /> : null}
          {onAbandon ? <MenuItem testID="task-menu-abandon" label="Abandon (give back to the team)" onPress={run(onAbandon)} /> : null}
          {onEdit ? <MenuItem testID="task-menu-edit" label="Edit" onPress={run(onEdit)} /> : null}
          {onComplete ? <MenuItem testID="task-menu-complete" label="Mark as done" onPress={run(onComplete)} /> : null}
          {onReopen ? <MenuItem testID="task-menu-reopen" label="Mark as not done" onPress={run(onReopen)} /> : null}
          {onDelete ? <MenuItem testID="task-menu-delete" label="Delete" danger onPress={run(onDelete)} /> : null}
          <MenuItem testID="task-menu-cancel" label="Cancel" muted onPress={onClose} />
        </View>
      </Pressable>
    </Modal>
  );
}

function MenuItem({ label, onPress, danger, muted, testID }: { label: string; onPress: () => void; danger?: boolean; muted?: boolean; testID: string }) {
  return (
    <Pressable testID={testID} onPress={onPress} style={styles.item}>
      <Text style={[styles.itemText, danger ? styles.danger : null, muted ? styles.muted : null]}>{label}</Text>
    </Pressable>
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
  title: { ...typography.subtitle, color: colors.text, marginBottom: spacing.xs },
  item: { paddingVertical: spacing.md },
  itemText: { ...typography.body, color: colors.text, fontWeight: '600' },
  danger: { color: colors.danger },
  muted: { color: colors.textMuted },
});
