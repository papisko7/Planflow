import { formatDuration, fromHours, toHours } from '../duration';

describe('toHours', () => {
  it('converts every unit to hours', () => {
    expect(toHours(90, 'minute')).toBeCloseTo(1.5);
    expect(toHours(3, 'hour')).toBe(3);
    expect(toHours(2, 'day')).toBe(48);
    expect(toHours(1, 'week')).toBe(168);
    expect(toHours(1, 'month')).toBe(720);
  });
});

describe('fromHours', () => {
  it('shows the largest unit that divides evenly', () => {
    expect(fromHours(48)).toEqual({ value: 2, unit: 'day' });
    expect(fromHours(168)).toEqual({ value: 1, unit: 'week' });
    expect(fromHours(1440)).toEqual({ value: 2, unit: 'month' });
    expect(fromHours(30)).toEqual({ value: 30, unit: 'hour' });
  });

  it('falls back to minutes for fractions of an hour', () => {
    expect(fromHours(1.5)).toEqual({ value: 90, unit: 'minute' });
    expect(fromHours(20 / 60)).toEqual({ value: 20, unit: 'minute' });
  });

  it('round-trips through toHours', () => {
    for (const h of [0.25, 1, 5, 24, 36, 100, 168, 720]) {
      const { value, unit } = fromHours(h);
      expect(toHours(value, unit)).toBeCloseTo(h);
    }
  });

  it('rounds to 2 decimals when no allowed unit divides evenly', () => {
    expect(fromHours(10, ['day', 'week'])).toEqual({ value: 0.42, unit: 'day' });
  });
});

describe('formatDuration', () => {
  it('formats compactly', () => {
    expect(formatDuration(48)).toBe('2 d');
    expect(formatDuration(1.5)).toBe('90 min');
  });
});
