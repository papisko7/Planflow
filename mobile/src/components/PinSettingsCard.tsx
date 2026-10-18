import { useEffect, useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Switch, Text, View } from 'react-native';
import { isPinEnabled, setPin, verifyPin, clearPin } from '../services/pinService';
import { PinEntryModal } from './PinEntryModal';
import { colors, radius, spacing, typography } from '../theme/theme';

type Mode = 'none' | 'setup-enter' | 'setup-confirm' | 'disable-verify' | 'change-verify' | 'change-enter' | 'change-confirm';

const MODAL_TITLES: Record<Mode, string> = {
  'none': '',
  'setup-enter': 'Choose a 4-digit PIN',
  'setup-confirm': 'Confirm your PIN',
  'disable-verify': 'Enter your PIN to disable',
  'change-verify': 'Enter your current PIN',
  'change-enter': 'Choose a new 4-digit PIN',
  'change-confirm': 'Confirm your new PIN',
};

// The PIN part of the Credentials section (the section itself provides the heading and the card):
// turn PIN login on/off and, once it is on, change the PIN. Changing asks for the current PIN first.
export function PinSettingsCard({ onEnabledChange }: { onEnabledChange?: (enabled: boolean) => void }) {
  const [enabled, setEnabledState] = useState(false);
  const [loading, setLoading] = useState(true);
  const [mode, setMode] = useState<Mode>('none');
  const [firstPin, setFirstPin] = useState('');
  const [pinError, setPinError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const setEnabled = (value: boolean) => {
    setEnabledState(value);
    onEnabledChange?.(value);
  };

  useEffect(() => {
    isPinEnabled().then((e) => {
      setEnabledState(e);
      onEnabledChange?.(e);
      setLoading(false);
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps -- load once on mount
  }, []);

  const onToggle = (value: boolean) => {
    setPinError(null);
    setNotice(null);
    setMode(value ? 'setup-enter' : 'disable-verify');
  };

  const onChangePin = () => {
    setPinError(null);
    setNotice(null);
    setMode('change-verify');
  };

  const onCancel = () => {
    setMode('none');
    setPinError(null);
    setFirstPin('');
  };

  const onComplete = async (pin: string) => {
    // Both "set a PIN" and "change the PIN" pick a new one twice; they differ only in how they start.
    if (mode === 'setup-enter' || mode === 'change-enter') {
      setFirstPin(pin);
      setMode(mode === 'setup-enter' ? 'setup-confirm' : 'change-confirm');
      setPinError(null);
      return;
    }

    if (mode === 'setup-confirm' || mode === 'change-confirm') {
      if (pin !== firstPin) {
        setPinError("PINs don't match. Try again.");
        return;
      }
      await setPin(pin);
      setEnabled(true);
      setNotice(mode === 'change-confirm' ? 'PIN changed.' : null);
      setMode('none');
      setFirstPin('');
      setPinError(null);
      return;
    }

    if (mode === 'change-verify') {
      if (!(await verifyPin(pin))) {
        setPinError('Incorrect PIN.');
        return;
      }
      setMode('change-enter');
      setPinError(null);
      return;
    }

    if (mode === 'disable-verify') {
      const valid = await verifyPin(pin);
      if (!valid) {
        setPinError('Incorrect PIN.');
        return;
      }
      await clearPin();
      setEnabled(false);
      setMode('none');
      setPinError(null);
    }
  };

  return (
    <View style={styles.container}>
      {loading ? (
        <ActivityIndicator color={colors.primary} />
      ) : (
        <>
          <View style={styles.row}>
            <View style={styles.labelGroup}>
              <Text style={styles.label}>4-digit PIN login</Text>
              <Text style={styles.hint}>Sign in with a short PIN instead of your password.</Text>
            </View>
            <Switch
              testID="pin-toggle"
              value={enabled}
              onValueChange={onToggle}
              trackColor={{ false: colors.border, true: colors.primary }}
              thumbColor={colors.surface}
            />
          </View>
          {enabled ? (
            <Pressable testID="pin-change-button" style={styles.outlineButton} onPress={onChangePin}>
              <Text style={styles.outlineButtonText}>Change PIN</Text>
            </Pressable>
          ) : null}
          {notice ? <Text style={styles.success}>{notice}</Text> : null}
        </>
      )}

      <PinEntryModal
        visible={mode !== 'none'}
        title={MODAL_TITLES[mode]}
        error={pinError}
        onComplete={onComplete}
        onCancel={onCancel}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container: { gap: spacing.xs },
  row: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  labelGroup: { flex: 1, marginRight: spacing.md },
  label: { ...typography.body, color: colors.text },
  hint: { fontSize: 12, color: colors.textMuted, marginTop: 2 },
  outlineButton: {
    borderWidth: 1,
    borderColor: colors.primary,
    borderRadius: radius.md,
    padding: spacing.sm,
    alignItems: 'center',
    marginTop: spacing.xs,
  },
  outlineButtonText: { color: colors.primary, fontWeight: '700' },
  success: { ...typography.body, color: colors.primary, fontWeight: '600' },
});
