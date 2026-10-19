// The backend stores a duration (the effort) as plain hours; the UI lets the user
// think in whatever unit is natural ("2 days", "90 minutes"). These helpers convert both ways.
export type DurationUnit = 'minute' | 'hour' | 'day' | 'week' | 'month';

// A "month" is a flat 30 days - good enough for an estimate, and it keeps the conversion exact both ways.
const HOURS_PER_UNIT: Record<DurationUnit, number> = {
  minute: 1 / 60,
  hour: 1,
  day: 24,
  week: 24 * 7,
  month: 24 * 30,
};

export const DURATION_UNIT_LABEL: Record<DurationUnit, { short: string; long: string }> = {
  minute: { short: 'min', long: 'Minutes' },
  hour: { short: 'h', long: 'Hours' },
  day: { short: 'd', long: 'Days' },
  week: { short: 'wk', long: 'Weeks' },
  month: { short: 'mo', long: 'Months' },
};

// Effort can be tiny (minutes) or huge (months).
export const EFFORT_UNITS: DurationUnit[] = ['minute', 'hour', 'day', 'week', 'month'];

export function toHours(value: number, unit: DurationUnit): number {
  return value * HOURS_PER_UNIT[unit];
}

const isWhole = (n: number) => Math.abs(n - Math.round(n)) < 1e-6;

// Picks the unit to show a stored hour count in: the largest allowed unit that divides it evenly
// (48h -> 2 days, 90 min -> 90 minutes). A value that fits no unit evenly is shown in the largest
// unit it exceeds, rounded to 2 decimals (10h in a days-only list -> 0.42 days).
export function fromHours(hours: number, units: DurationUnit[] = EFFORT_UNITS): { value: number; unit: DurationUnit } {
  const bigToSmall = [...units].sort((a, b) => HOURS_PER_UNIT[b] - HOURS_PER_UNIT[a]);
  const even = bigToSmall.find((u) => isWhole(hours / HOURS_PER_UNIT[u]));
  const unit = even ?? bigToSmall.find((u) => hours >= HOURS_PER_UNIT[u]) ?? bigToSmall[bigToSmall.length - 1];
  const value = hours / HOURS_PER_UNIT[unit];
  return { value: even ? Math.round(value) : Math.round(value * 100) / 100, unit };
}

// "2 d", "90 min" - the compact form used on cards and in the detail view.
export function formatDuration(hours: number, units: DurationUnit[] = EFFORT_UNITS): string {
  const { value, unit } = fromHours(hours, units);
  return `${value} ${DURATION_UNIT_LABEL[unit].short}`;
}
