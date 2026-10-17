import { useEffect, useRef, useState } from 'react';
import { Keyboard, Modal, Pressable, StyleSheet, Text, TextInput, View } from 'react-native';
import { colors, radius, spacing, typography } from '../theme/theme';

type Props = {
  visible: boolean;
  title: string;
  error?: string | null;
  onComplete: (pin: string) => void;
  onCancel: () => void;
};

export function PinEntryModal({ visible, title, error, onComplete, onCancel }: Props) {
  const [pin, setPin] = useState('');
  const inputRef = useRef<TextInput>(null);

  useEffect(() => {
    if (visible) {
      setPin('');
      const t = setTimeout(() => inputRef.current?.focus(), 150);
      return () => clearTimeout(t);
    }
  }, [visible, title]);

  useEffect(() => {
    if (error) setPin('');
  }, [error]);

  const handleChange = (text: string) => {
    const digits = text.replace(/\D/g, '').slice(0, 4);
    setPin(digits);
    if (digits.length === 4) onComplete(digits);
  };

  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={onCancel}>
      <Pressable style={styles.overlay} onPress={Keyboard.dismiss} accessible={false}>
        <View style={styles.card}>
          <Text style={styles.title}>{title}</Text>

          {/* Hidden input that drives the keyboard */}
          <TextInput
            ref={inputRef}
            style={styles.hiddenInput}
            value={pin}
            onChangeText={handleChange}
            keyboardType="number-pad"
            maxLength={4}
            caretHidden
          />

          {/* Visual PIN dots — tap to re-focus keyboard */}
          <Pressable style={styles.dots} onPress={() => inputRef.current?.focus()}>
            {[0, 1, 2, 3].map((i) => (
              <View key={i} style={[styles.dot, pin.length > i && styles.dotFilled]} />
            ))}
          </Pressable>

          {error ? <Text style={styles.error}>{error}</Text> : null}

          <Pressable onPress={onCancel} style={styles.cancelButton}>
            <Text style={styles.cancelText}>Cancel</Text>
          </Pressable>
        </View>
      </Pressable>
    </Modal>
  );
}

const styles = StyleSheet.create({
  overlay: {
    flex: 1,
    backgroundColor: 'rgba(0,0,0,0.5)',
    justifyContent: 'center',
    alignItems: 'center',
    padding: spacing.lg,
  },
  card: {
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    padding: spacing.xl,
    width: '100%',
    alignItems: 'center',
    gap: spacing.md,
  },
  title: { ...typography.subtitle, color: colors.text, fontWeight: '700', textAlign: 'center' },
  hiddenInput: { position: 'absolute', opacity: 0, height: 0, width: 0 },
  dots: { flexDirection: 'row', gap: spacing.lg, paddingVertical: spacing.md },
  dot: {
    width: 18,
    height: 18,
    borderRadius: 9,
    borderWidth: 2,
    borderColor: colors.primary,
    backgroundColor: 'transparent',
  },
  dotFilled: { backgroundColor: colors.primary },
  error: { ...typography.body, color: colors.danger, textAlign: 'center' },
  cancelButton: { padding: spacing.sm, marginTop: spacing.xs },
  cancelText: { color: colors.textMuted, fontSize: 14 },
});
