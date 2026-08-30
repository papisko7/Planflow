import { StyleSheet, Text, View } from 'react-native';
import { colors, radius, spacing, typography } from '../theme/theme';
import type { UrgencyScoreBreakdownDto } from '../types/task';

// Mirrors the weights hard-coded server-side (see UrgencyScoreLog.cs):
// FinalScore = 0.35*Deadline + 0.20*Ai + 0.15*Blocking + 0.20*Impact + 0.10*UserOverride.
// Shown next to each bar so the breakdown UI doubles as documentation of the formula for the thesis.
const COMPONENTS: Array<{ key: keyof UrgencyScoreBreakdownDto; label: string; weight: number; color: string }> = [
  { key: 'deadlineComponent', label: 'Deadline', weight: 0.35, color: colors.urgencyCritical },
  { key: 'impactComponent', label: 'Impact', weight: 0.2, color: colors.primary },
  { key: 'aiComponent', label: 'AI Assessment', weight: 0.2, color: colors.urgencyHigh },
  { key: 'blockingComponent', label: 'Blocking', weight: 0.15, color: colors.urgencyMedium },
  { key: 'userOverrideComponent', label: 'Manual Override', weight: 0.1, color: colors.textMuted },
];

function formatComputedAt(createdAtUtc: string): string {
  return new Date(createdAtUtc).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

export interface UrgencyBreakdownProps {
  breakdown: UrgencyScoreBreakdownDto;
}

export function UrgencyBreakdown({ breakdown }: UrgencyBreakdownProps) {
  return (
    <View style={styles.container}>
      <View style={styles.finalScoreRow}>
        <Text style={styles.finalScoreLabel}>Final Urgency Score</Text>
        <Text style={styles.finalScoreValue}>{breakdown.finalScore.toFixed(2)}</Text>
      </View>

      {COMPONENTS.map(({ key, label, weight, color }) => {
        const value = breakdown[key] as number;
        return (
          <View key={key} style={styles.row}>
            <View style={styles.rowHeader}>
              <Text style={styles.rowLabel}>
                {label} <Text style={styles.rowWeight}>×{weight.toFixed(2)}</Text>
              </Text>
              <Text style={styles.rowValue}>{value.toFixed(2)}</Text>
            </View>
            <View style={styles.barTrack}>
              <View style={[styles.barFill, { width: `${Math.min(1, Math.max(0, value)) * 100}%`, backgroundColor: color }]} />
            </View>
          </View>
        );
      })}

      <View style={styles.footerRow}>
        {breakdown.aiFallbackUsed ? (
          <Text style={styles.fallbackBadge}>⚠️ AI fallback used for this computation</Text>
        ) : null}
        <Text style={styles.computedAt}>Computed {formatComputedAt(breakdown.createdAtUtc)}</Text>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    backgroundColor: colors.surface,
    borderRadius: radius.md,
    padding: spacing.md,
    gap: spacing.sm,
  },
  finalScoreRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingBottom: spacing.sm,
    borderBottomWidth: 1,
    borderBottomColor: colors.border,
    marginBottom: spacing.xs,
  },
  finalScoreLabel: { ...typography.subtitle, color: colors.text },
  finalScoreValue: { ...typography.title, color: colors.primary },
  row: { gap: 4 },
  rowHeader: { flexDirection: 'row', justifyContent: 'space-between' },
  rowLabel: { ...typography.body, color: colors.text },
  rowWeight: { color: colors.textMuted, fontSize: 12 },
  rowValue: { ...typography.body, color: colors.textMuted, fontVariant: ['tabular-nums'] },
  barTrack: {
    height: 8,
    borderRadius: radius.sm,
    backgroundColor: colors.background,
    overflow: 'hidden',
  },
  barFill: { height: '100%', borderRadius: radius.sm },
  footerRow: { marginTop: spacing.xs, gap: 4 },
  fallbackBadge: { color: colors.urgencyHigh, fontSize: 12, fontWeight: '600' },
  computedAt: { color: colors.textMuted, fontSize: 12 },
});
