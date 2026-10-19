import { useState, type ReactNode } from 'react';
import { LayoutAnimation, Pressable, StyleSheet, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing, typography } from '../theme/theme';

type Props = {
  title: string;
  summary?: string;
  nested?: boolean;
  defaultExpanded?: boolean;
  onHeaderLongPress?: () => void;
  testID: string;
  children: ReactNode;
};

// An accordion (collapsible section): a tappable header that shows or hides its content. Collapsed by default.
// The content stays mounted while collapsed (only hidden), so a half-typed form or a loading request is not lost.
export function AccordionSection({ title, summary, nested = false, defaultExpanded = false, onHeaderLongPress, testID, children }: Props) {
  const [expanded, setExpanded] = useState(defaultExpanded);

  const toggle = () => {
    LayoutAnimation.configureNext(LayoutAnimation.Presets.easeInEaseOut);
    setExpanded((current) => !current);
  };

  return (
    <View style={nested ? styles.nested : styles.card}>
      <Pressable
        testID={`${testID}-header`}
        onPress={toggle}
        onLongPress={onHeaderLongPress}
        accessibilityRole="button"
        accessibilityLabel={title}
        accessibilityState={{ expanded }}
        style={styles.header}
      >
        <View style={styles.headerText}>
          <Text style={nested ? styles.nestedTitle : styles.title}>{title}</Text>
          {!expanded && summary ? <Text style={styles.summary} numberOfLines={1}>{summary}</Text> : null}
        </View>
        <Ionicons name={expanded ? 'chevron-up' : 'chevron-down'} size={20} color={colors.textMuted} />
      </Pressable>
      <View testID={`${testID}-body`} style={expanded ? styles.body : styles.hidden}>
        {children}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  card: { backgroundColor: colors.surface, borderRadius: radius.md, paddingHorizontal: spacing.md },
  nested: { borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: colors.border, marginTop: spacing.xs },
  header: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: spacing.sm, paddingVertical: spacing.md },
  headerText: { flex: 1 },
  title: { ...typography.subtitle, color: colors.text },
  nestedTitle: { ...typography.body, color: colors.text, fontWeight: '600' },
  summary: { ...typography.body, fontSize: 12, color: colors.textMuted, marginTop: 2 },
  body: { gap: spacing.xs, paddingBottom: spacing.md },
  hidden: { display: 'none' },
});
