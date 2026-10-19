import { RecurrenceFrequency } from '../../types/task';
import { defaultRecurrence, formatRecurrence, recurrenceChanged, validateRecurrence, weekdayBit, WEEKDAYS } from '../recurrence';

const MON = 1;
const WED = 4;
const SUN = 64;

const rule = (over: Partial<Parameters<typeof formatRecurrence>[0]> = {}) => ({
  isRecurring: true,
  recurrenceFrequency: RecurrenceFrequency.Daily as RecurrenceFrequency | null,
  recurrenceInterval: 1 as number | null,
  recurrenceWeekdays: null as number | null,
  ...over,
});

describe('formatRecurrence', () => {
  it('says nothing for an item that does not repeat by a rule of its own', () => {
    expect(formatRecurrence(rule({ isRecurring: false }))).toBeNull();
    expect(formatRecurrence(rule({ recurrenceFrequency: null }))).toBeNull();
  });

  it('reads like a sentence for each frequency', () => {
    expect(formatRecurrence(rule())).toBe('every day');
    expect(formatRecurrence(rule({ recurrenceInterval: 3 }))).toBe('every 3 days');
    expect(formatRecurrence(rule({ recurrenceFrequency: RecurrenceFrequency.Monthly }))).toBe('every month');
    expect(formatRecurrence(rule({ recurrenceFrequency: RecurrenceFrequency.Yearly, recurrenceInterval: 2 }))).toBe('every 2 years');
  });

  it('names the days of a weekly rule, Monday first', () => {
    expect(formatRecurrence(rule({ recurrenceFrequency: RecurrenceFrequency.Weekly, recurrenceWeekdays: WED | MON }))).toBe('every week on Mon, Wed');
    expect(formatRecurrence(rule({ recurrenceFrequency: RecurrenceFrequency.Weekly, recurrenceInterval: 2, recurrenceWeekdays: SUN }))).toBe(
      'every 2 weeks on Sun',
    );
  });
});

describe('weekdayBit', () => {
  it('numbers the week from Monday, as the backend does', () => {
    expect(weekdayBit(new Date(2026, 9, 19))).toBe(MON); // Monday 19 Oct 2026
    expect(weekdayBit(new Date(2026, 9, 25))).toBe(SUN);
    expect(WEEKDAYS.map((d) => d.bit)).toEqual([1, 2, 4, 8, 16, 32, 64]);
  });
});

describe('defaultRecurrence', () => {
  it('starts daily, with the weekday of the first occurrence ready for a weekly rule', () => {
    const wednesday = new Date(2026, 9, 21, 9, 0).toISOString();
    expect(defaultRecurrence(wednesday)).toEqual({
      isRecurring: true,
      recurrenceFrequency: RecurrenceFrequency.Daily,
      recurrenceInterval: 1,
      recurrenceWeekdays: WED,
    });
    expect(defaultRecurrence(null).recurrenceWeekdays).toBeNull();
  });
});

describe('recurrenceChanged', () => {
  const stored = { ...rule({ recurrenceFrequency: RecurrenceFrequency.Weekly, recurrenceWeekdays: MON }), recurrenceEndsAtUtc: null as string | null };

  it('is false for the same rule, true for any part of it changed', () => {
    expect(recurrenceChanged(stored, { ...stored })).toBe(false);
    expect(recurrenceChanged(stored, { ...stored, recurrenceInterval: 2 })).toBe(true);
    expect(recurrenceChanged(stored, { ...stored, recurrenceFrequency: RecurrenceFrequency.Daily })).toBe(true);
    expect(recurrenceChanged(stored, { ...stored, recurrenceWeekdays: MON | WED })).toBe(true);
    expect(recurrenceChanged(stored, { ...stored, recurrenceEndsAtUtc: '2027-01-01T00:00:00.000Z' })).toBe(true);
    expect(recurrenceChanged(stored, { ...stored, isRecurring: false })).toBe(true);
  });

  it('ignores the weekdays of a rule that is not weekly', () => {
    const daily = { ...stored, recurrenceFrequency: RecurrenceFrequency.Daily, recurrenceWeekdays: null };
    expect(recurrenceChanged(daily, { ...daily, recurrenceWeekdays: WED })).toBe(false);
  });
});

describe('validateRecurrence', () => {
  it('asks for a frequency, an interval between 1 and 99, and a day for a weekly rule', () => {
    expect(validateRecurrence({ recurrenceFrequency: null, recurrenceInterval: 1, recurrenceWeekdays: null })).toMatch(/how often/);
    expect(validateRecurrence({ recurrenceFrequency: RecurrenceFrequency.Daily, recurrenceInterval: null, recurrenceWeekdays: null })).toMatch(/1 to 99/);
    expect(validateRecurrence({ recurrenceFrequency: RecurrenceFrequency.Daily, recurrenceInterval: 0, recurrenceWeekdays: null })).toMatch(/1 to 99/);
    expect(validateRecurrence({ recurrenceFrequency: RecurrenceFrequency.Weekly, recurrenceInterval: 1, recurrenceWeekdays: 0 })).toMatch(/day of the week/);
    expect(validateRecurrence({ recurrenceFrequency: RecurrenceFrequency.Weekly, recurrenceInterval: 1, recurrenceWeekdays: MON })).toBeNull();
    expect(validateRecurrence({ recurrenceFrequency: RecurrenceFrequency.Monthly, recurrenceInterval: 99, recurrenceWeekdays: null })).toBeNull();
  });
});
