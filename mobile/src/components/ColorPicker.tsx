import { Pressable, StyleSheet, Text, View } from 'react-native';
import { colors, radius, spacing, typography } from '../theme/theme';
import { colorHex, colorLabel, PALETTE, readableTextColor } from '../utils/palette';

type Props = {
  value: string | null;
  onChange: (colorKey: string | null) => void;
};

export function ColorPicker({ value, onChange }: Props) {
  return (
    <View style={styles.wrapper}>
      <Text style={styles.label}>Colour: {colorLabel(value)}</Text>
      <View style={styles.grid}>
        <Pressable
          testID="color-swatch-default"
          accessibilityLabel="Default colour"
          onPress={() => onChange(null)}
          style={[styles.swatch, styles.defaultSwatch, value === null ? styles.selected : null]}
        >
          {value === null ? <Text style={styles.defaultTick}>✓</Text> : null}
        </Pressable>
        {PALETTE.map((color) => (
          <Pressable
            key={color.key}
            testID={`color-swatch-${color.key}`}
            accessibilityLabel={color.label}
            onPress={() => onChange(color.key)}
            style={[styles.swatch, { backgroundColor: color.hex }, value === color.key ? styles.selected : null]}
          >
            {value === color.key ? <Text style={[styles.tick, { color: readableTextColor(colorHex(color.key)!) }]}>✓</Text> : null}
          </Pressable>
        ))}
      </View>
    </View>
  );
}

const SIZE = 30;

const styles = StyleSheet.create({
  wrapper: { gap: spacing.xs },
  label: { ...typography.body, color: colors.textMuted },
  grid: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  swatch: { width: SIZE, height: SIZE, borderRadius: radius.lg, alignItems: 'center', justifyContent: 'center', borderWidth: 2, borderColor: 'transparent' },
  defaultSwatch: { backgroundColor: colors.background, borderColor: colors.border },
  selected: { borderColor: colors.text },
  tick: { fontWeight: '700', fontSize: 14 },
  defaultTick: { fontWeight: '700', fontSize: 14, color: colors.text },
});
