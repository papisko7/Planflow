import { StyleSheet, Text, View } from 'react-native';
import { colors, radius, spacing, typography } from '../theme/theme';
import type { UrgencyScoreBreakdownDto } from '../types/task';
import { splitReason } from '../utils/itemDisplay';

// Mirrors the weights hard-coded server-side (see UrgencyScoreCalculator.cs):
// weighted sum 0.40*Deadline + 0.20*Blocking + 0.25*Impact + 0.10*UserOverride + 0.05*Scheduled (algorithm v2), then two floors
// (due within 24h -> at least 0.85, except for an unverified item; a blocker is never below what it blocks) and, for a blocked task, x0.5.
// A factor that does not apply (unrated impact, no planned start) hands its weight to the others.
// Shown next to each bar so the breakdown UI doubles as documentation of the formula for the thesis.
const COMPONENTS: Array<{ key: keyof UrgencyScoreBreakdownDto; label: string; weight: number; color: string }> = [
  { key: 'deadlineComponent', label: 'Deadline', weight: 0.4, color: colors.urgencyCritical },
  { key: 'impactComponent', label: 'Impact', weight: 0.25, color: colors.primary },
  { key: 'blockingComponent', label: 'Blocks urgent work', weight: 0.2, color: colors.urgencyMedium },
  { key: 'userOverrideComponent', label: 'Manual Override', weight: 0.1, color: colors.textMuted },
  { key: 'scheduledComponent', label: 'Planned start', weight: 0.05, color: colors.primary },
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
  const reason = splitReason(breakdown.reasonText);
  return (
    <View style={styles.container}>
      {reason ? (
        <Text testID="urgency-reason" style={styles.reason}>
          {reason.label ? <Text style={styles.reasonLabel}>{reason.label} </Text> : null}
          {reason.rest}
        </Text>
      ) : null}

      <View style={styles.finalScoreRow}>
        <Text style={styles.finalScoreLabel}>Final Urgency Score</Text>
        <Text style={styles.finalScoreValue}>{breakdown.finalScore.toFixed(2)}</Text>
      </View>

      {COMPONENTS.map(({ key, label, weight, color }) => {
        const value = (breakdown[key] ?? null) as number | null;
        if (key === 'scheduledComponent' && value === null) return null;
        if (value === null) {
          return (
            <View key={key} style={styles.row}>
              <View style={styles.rowHeader}>
                <Text style={styles.rowLabel}>{label}</Text>
                <Text style={styles.rowValue}>not rated</Text>
              </View>
            </View>
          );
        }
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

      {breakdown.isBlocked ? (
        <Text style={styles.blockedNote}>Waiting on an unfinished task, so this score is halved until it is done.</Text>
      ) : null}

      <View style={styles.footerRow}>
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
  reason: { ...typography.body, color: colors.text },
  reasonLabel: { fontWeight: '700', color: colors.primary },
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
  blockedNote: { color: colors.urgencyMedium, fontSize: 12 },
  computedAt: { color: colors.textMuted, fontSize: 12 },
});
