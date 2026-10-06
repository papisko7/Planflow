import { NotificationEvent } from '../../types/notification';
import { notificationIcon, timeAgo } from '../notificationDisplay';

describe('timeAgo', () => {
  const now = new Date('2026-10-06T12:00:00Z');

  it.each([
    ['2026-10-06T11:59:40Z', 'just now'],
    ['2026-10-06T11:55:00Z', '5 min ago'],
    ['2026-10-06T09:00:00Z', '3 h ago'],
    ['2026-10-05T10:00:00Z', 'yesterday'],
    ['2026-10-02T12:00:00Z', '4 days ago'],
  ])('%s -> %s', (iso, expected) => {
    expect(timeAgo(iso, now)).toBe(expected);
  });

  it('treats a timestamp without a zone as UTC (what the server sends)', () => {
    expect(timeAgo('2026-10-06T11:55:00', now)).toBe('5 min ago');
  });

  it('falls back to the date for old entries', () => {
    expect(timeAgo('2026-09-01T12:00:00Z', now)).toBe(new Date('2026-09-01T12:00:00Z').toLocaleDateString());
  });
});

describe('notificationIcon', () => {
  it('gives every inbox event an icon', () => {
    const events = Object.values(NotificationEvent).filter((v): v is NotificationEvent => typeof v === 'number');
    for (const event of events) {
      expect(notificationIcon(event).name).toBeTruthy();
    }
  });
});
