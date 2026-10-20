import { StyleSheet, Text, View } from 'react-native';
import { colors, spacing, typography } from '../theme/theme';

type Props = {
  title: string;
  hint?: string;
};

// The caption above a form field: what the field is and, in muted text below it, what it is used for. The fields themselves carry
// no placeholder text, so the caption stays visible while typing and when an existing item is opened for editing.
export function FieldLabel({ title, hint }: Props) {
  return (
    <View style={styles.wrapper}>
      <Text style={styles.title}>{title}</Text>
      {hint ? <Text style={styles.hint}>{hint}</Text> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: { gap: 2 },
  title: { ...typography.body, color: colors.text, fontWeight: '600', marginTop: spacing.xs },
  hint: { ...typography.body, fontSize: 12, color: colors.textMuted },
});
