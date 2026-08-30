import type { TaskDto } from '../types/task';

// Thresholds mirror the backend's UrgencyScoreLog.FinalScore range (0..1) — kept client-side
// since they're purely a display concern, not part of the score algorithm itself.
export type UrgencyTier = 'critical' | 'high' | 'medium' | 'low';

export const URGENCY_TIER_META: Record<UrgencyTier, { label: string; badge: string; minScore: number }> = {
  critical: { label: 'Critical', badge: '🔴', minScore: 0.8 },
  high: { label: 'High', badge: '🟡', minScore: 0.6 },
  medium: { label: 'Medium', badge: '🟢', minScore: 0.4 },
  low: { label: 'Low', badge: '⚪', minScore: 0 },
};

export function getUrgencyTier(score: number): UrgencyTier {
  if (score >= URGENCY_TIER_META.critical.minScore) return 'critical';
  if (score >= URGENCY_TIER_META.high.minScore) return 'high';
  if (score >= URGENCY_TIER_META.medium.minScore) return 'medium';
  return 'low';
}

export interface UrgencySection {
  tier: UrgencyTier;
  title: string;
  data: TaskDto[];
}

const TIER_ORDER: UrgencyTier[] = ['critical', 'high', 'medium', 'low'];

// Groups tasks into the four urgency tiers, sorted by score descending within each tier,
// and drops any tier that has no tasks so the SectionList doesn't render empty headers.
export function groupTasksByUrgency(tasks: TaskDto[]): UrgencySection[] {
  const buckets: Record<UrgencyTier, TaskDto[]> = { critical: [], high: [], medium: [], low: [] };

  for (const task of tasks) {
    buckets[getUrgencyTier(task.currentUrgencyScore)].push(task);
  }

  return TIER_ORDER.filter((tier) => buckets[tier].length > 0).map((tier) => ({
    tier,
    title: `${URGENCY_TIER_META[tier].badge} ${URGENCY_TIER_META[tier].label} (${buckets[tier].length})`,
    data: buckets[tier].sort((a, b) => b.currentUrgencyScore - a.currentUrgencyScore),
  }));
}
