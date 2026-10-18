import { Modal, Pressable, StyleSheet, Text, View } from 'react-native';
import { colors, radius, spacing, typography } from '../theme/theme';

export type SheetOption = { key: string; label: string; testID: string; danger?: boolean; checked?: boolean };

type Props = {
  visible: boolean;
  title: string;
  options: SheetOption[];
  onSelect: (key: string) => void;
  onClose: () => void;
  // Shown when there is nothing to pick (e.g. "No lists for tasks yet").
  emptyText?: string;
};

// A bottom sheet with a short list of choices (a status, a list, a kind of "Clear"). Closes first, then runs the choice,
// so an Alert or the next sheet never opens underneath it - the same way as the task action menu.
export function OptionSheet({ visible, title, options, onSelect, onClose, emptyText }: Props) {
  return (
    <Modal transparent animationType="fade" visible={visible} onRequestClose={onClose}>
      <Pressable style={styles.backdrop} onPress={onClose}>
        <View style={styles.sheet}>
          <Text style={styles.title} numberOfLines={1}>
            {title}
          </Text>
          {options.length === 0 && emptyText ? <Text style={styles.empty}>{emptyText}</Text> : null}
          {options.map((option) => (
            <Pressable
              key={option.key}
              testID={option.testID}
              style={styles.item}
              onPress={() => {
                onClose();
                onSelect(option.key);
              }}
            >
              <Text style={[styles.itemText, option.danger ? styles.danger : null, option.checked ? styles.checked : null]}>
                {option.checked ? '✓ ' : ''}
                {option.label}
              </Text>
            </Pressable>
          ))}
          <Pressable testID="option-sheet-cancel" style={styles.item} onPress={onClose}>
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
  title: { ...typography.subtitle, color: colors.text, marginBottom: spacing.xs },
  empty: { ...typography.body, color: colors.textMuted },
  item: { paddingVertical: spacing.md },
  itemText: { ...typography.body, color: colors.text, fontWeight: '600' },
  danger: { color: colors.danger },
  checked: { color: colors.primary },
  muted: { color: colors.textMuted },
});
