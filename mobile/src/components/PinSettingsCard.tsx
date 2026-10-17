import { useEffect, useState } from 'react';
import { ActivityIndicator, StyleSheet, Switch, Text, View } from 'react-native';
import { isPinEnabled, setPin, verifyPin, clearPin } from '../services/pinService';
import { PinEntryModal } from './PinEntryModal';
import { colors, radius, spacing, typography } from '../theme/theme';

type Mode = 'none' | 'setup-enter' | 'setup-confirm' | 'disable-verify';

const MODAL_TITLES: Record<Mode, string> = {
  'none': '',
  'setup-enter': 'Choose a 4-digit PIN',
  'setup-confirm': 'Confirm your PIN',
  'disable-verify': 'Enter your PIN to disable',
};

export function PinSettingsCard() {
  const [enabled, setEnabled] = useState(false);
  const [loading, setLoading] = useState(true);
  const [mode, setMode] = useState<Mode>('none');
  const [firstPin, setFirstPin] = useState('');
  const [pinError, setPinError] = useState<string | null>(null);

  useEffect(() => {
    isPinEnabled().then((e) => { setEnabled(e); setLoading(false); });
  }, []);

  const onToggle = (value: boolean) => {
    setPinError(null);
    setMode(value ? 'setup-enter' : 'disable-verify');
  };

  const onCancel = () => {
    setMode('none');
    setPinError(null);
    setFirstPin('');
  };

  const onComplete = async (pin: string) => {
    if (mode === 'setup-enter') {
      setFirstPin(pin);
      setMode('setup-confirm');
      setPinError(null);
      return;
    }

    if (mode === 'setup-confirm') {
      if (pin !== firstPin) {
        setPinError("PINs don't match. Try again.");
        return;
      }
      await setPin(pin);
      setEnabled(true);
      setMode('none');
      setFirstPin('');
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
    <View style={styles.card}>
      <Text style={styles.sectionTitle}>PIN login</Text>

      {loading ? (
        <ActivityIndicator color={colors.primary} />
      ) : (
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
  card: { backgroundColor: colors.surface, borderRadius: radius.md, padding: spacing.md, gap: spacing.xs },
  sectionTitle: { ...typography.subtitle, color: colors.text, marginBottom: spacing.xs },
  row: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  labelGroup: { flex: 1, marginRight: spacing.md },
  label: { ...typography.body, color: colors.text },
  hint: { fontSize: 12, color: colors.textMuted, marginTop: 2 },
});
