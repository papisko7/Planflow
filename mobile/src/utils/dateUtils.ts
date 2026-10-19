export function dayKey(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

export function startOfDay(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate());
}

export function addDays(d: Date, days: number): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate() + days);
}

export function buildMonthGrid(year: number, month: number): (Date | null)[][] {
  const daysInMonth = new Date(year, month + 1, 0).getDate();
  const leading = (new Date(year, month, 1).getDay() + 6) % 7; // Sunday=0 -> 6, Monday=1 -> 0
  const cells: (Date | null)[] = [
    ...Array<null>(leading).fill(null),
    ...Array.from({ length: daysInMonth }, (_, i) => new Date(year, month, i + 1)),
  ];
  while (cells.length % 7 !== 0) cells.push(null);

  const weeks: (Date | null)[][] = [];
  for (let i = 0; i < cells.length; i += 7) weeks.push(cells.slice(i, i + 7));
  return weeks;
}

// "All day" is stored as the very end of the chosen day (23:59 local), so a task due "today" is not
// instantly overdue. A concrete time ("HH:MM") is used as is.
export const ALL_DAY = null;

export function composeDeadline(day: Date, time: string | null): string {
  const [h, m] = time ? time.split(':').map(Number) : [23, 59];
  return new Date(day.getFullYear(), day.getMonth(), day.getDate(), h, m).toISOString();
}

export function splitDeadline(iso: string): { day: Date; time: string | null } {
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  const isAllDay = d.getHours() === 23 && d.getMinutes() === 59;
  return { day: startOfDay(d), time: isAllDay ? ALL_DAY : `${pad(d.getHours())}:${pad(d.getMinutes())}` };
}

export function formatDay(iso: string): string {
  return startOfDay(new Date(iso)).toLocaleDateString(undefined, { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' });
}

export function formatDeadline(iso: string): string {
  const { time } = splitDeadline(iso);
  return `${formatDay(iso)} · ${time ?? 'all day'}`;
}

// Pinch = two fingers moving together or apart. Comparing the finger distance at the start and at the
// end of the gesture tells which one it was; small movements are ignored so a sloppy tap is not a zoom.
export type PinchDirection = 'in' | 'out' | null;

export function pinchDirection(startDistance: number, endDistance: number): PinchDirection {
  if (startDistance <= 0) return null;
  const ratio = endDistance / startDistance;
  if (ratio <= 0.75) return 'in';
  if (ratio >= 1.33) return 'out';
  return null;
}
