import { buildMonthGrid, composeDeadline, dayKey, pinchDirection, splitDeadline } from '../dateUtils';

describe('buildMonthGrid', () => {
  it('lays October 2026 out Monday-first with null padding', () => {
    const weeks = buildMonthGrid(2026, 9); // Oct 1, 2026 is a Thursday
    expect(weeks.every((w) => w.length === 7)).toBe(true);
    expect(weeks[0].slice(0, 3)).toEqual([null, null, null]);
    expect(weeks[0][3]).toEqual(new Date(2026, 9, 1));
    expect(weeks.flat().filter(Boolean)).toHaveLength(31);
  });

  it('starts a month that begins on Monday with no padding', () => {
    const weeks = buildMonthGrid(2026, 5); // Jun 1, 2026 is a Monday
    expect(weeks[0][0]).toEqual(new Date(2026, 5, 1));
  });
});

describe('deadline composition', () => {
  it('stores "all day" as 23:59 local and reads it back as all day', () => {
    const iso = composeDeadline(new Date(2026, 9, 12), null);
    expect(splitDeadline(iso)).toEqual({ day: new Date(2026, 9, 12), time: null });
  });

  it('keeps a chosen time', () => {
    const iso = composeDeadline(new Date(2026, 9, 12), '15:00');
    const { day, time } = splitDeadline(iso);
    expect(dayKey(day)).toBe('2026-10-12');
    expect(time).toBe('15:00');
  });
});

describe('pinchDirection', () => {
  it('recognises fingers moving together as "in" and apart as "out"', () => {
    expect(pinchDirection(200, 120)).toBe('in');
    expect(pinchDirection(100, 180)).toBe('out');
  });

  it('ignores small movements and a missing start distance', () => {
    expect(pinchDirection(100, 110)).toBeNull();
    expect(pinchDirection(0, 100)).toBeNull();
  });
});
