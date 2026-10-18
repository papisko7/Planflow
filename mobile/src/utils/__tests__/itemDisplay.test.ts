import { EventTemplate, TaskType } from '../../types/task';
import { formatColorKey, formatReminder, formatWhen, TEMPLATE_LABEL } from '../itemDisplay';

const base = { taskType: TaskType.Event, deadlineUtc: null };

describe('formatWhen', () => {
  it('shows a task by its deadline day', () => {
    const text = formatWhen({ taskType: TaskType.Task, deadlineUtc: new Date(2026, 9, 20, 12).toISOString(), startUtc: null, endUtc: null });
    expect(text).toMatch(/20/);
  });

  it("shows a one-day imported all-day event as its date plus 'all day', whatever the viewer's zone", () => {
    const text = formatWhen({ ...base, startUtc: '2026-10-20T00:00:00Z', endUtc: '2026-10-21T00:00:00Z', isAllDay: true });
    expect(text).toMatch(/20/);
    expect(text).toMatch(/all day$/);
    expect(text).not.toMatch(/21/); // the stored end is the midnight after the last day; it must not show as a second day
  });

  it('shows a multi-day imported all-day event as first - last day', () => {
    const text = formatWhen({ ...base, startUtc: '2026-10-20T00:00:00Z', endUtc: '2026-10-23T00:00:00Z', isAllDay: true });
    expect(text).toMatch(/20.*-.*22.*all day$/);
  });

  it('still shows an older all-day event (recognised by its shape)', () => {
    const start = new Date(2026, 9, 20, 0, 0).toISOString();
    const end = new Date(2026, 9, 20, 23, 59).toISOString();
    expect(formatWhen({ ...base, startUtc: start, endUtc: end })).toMatch(/all day$/);
  });

  it('shows a timed event as day and clock range', () => {
    const start = new Date(2026, 9, 20, 14, 0).toISOString();
    const end = new Date(2026, 9, 20, 15, 30).toISOString();
    expect(formatWhen({ ...base, startUtc: start, endUtc: end })).toMatch(/14:00-15:30$/);
  });
});

describe('formatReminder', () => {
  const offset = (offsetMinutes: number) => ({ offsetMinutes, daysBefore: null, timeOfDayMinutes: null });
  const day = (daysBefore: number, timeOfDayMinutes: number) => ({ offsetMinutes: null, daysBefore, timeOfDayMinutes });

  it.each([
    [0, 'At the start'],
    [1, '1 minute before'],
    [10, '10 minutes before'],
    [60, '1 hour before'],
    [120, '2 hours before'],
    [90, '90 minutes before'],
    [1440, '1 day before'],
    [10080, '7 days before'],
  ])('offset %i minutes reads "%s"', (minutes, expected) => {
    expect(formatReminder(offset(minutes))).toBe(expected);
  });

  it('writes the day form with a time of day', () => {
    expect(formatReminder(day(1, 540))).toBe('1 day before at 09:00');
    expect(formatReminder(day(3, 0))).toBe('3 days before at 00:00');
    expect(formatReminder(day(0, 570))).toBe('On the day at 09:30');
  });
});

describe('template and colour labels', () => {
  it('labels special templates and leaves an ordinary event unlabelled', () => {
    expect(TEMPLATE_LABEL[EventTemplate.Default]).toBeNull();
    expect(TEMPLATE_LABEL[EventTemplate.OutOfOffice]).toBe('Out of office');
    expect(TEMPLATE_LABEL[EventTemplate.FocusTime]).toBe('Focus time');
    expect(TEMPLATE_LABEL[EventTemplate.WorkingLocation]).toBe('Working location');
    expect(TEMPLATE_LABEL[EventTemplate.Birthday]).toBe('Birthday');
  });

  it('turns a palette key into readable text', () => {
    expect(formatColorKey('tomato')).toBe('Tomato');
    expect(formatColorKey('cherry-blossom')).toBe('Cherry blossom');
  });
});
