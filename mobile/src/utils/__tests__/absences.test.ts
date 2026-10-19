import { TaskStatus, TaskType } from '../../types/task';
import type { MyAbsence } from '../../types/user';
import { deadlineInAbsence } from '../absences';

const NOW = new Date('2026-10-19T08:00:00Z');
const away = (start: string, end: string): MyAbsence => ({ itemId: 'a1', startUtc: start, endUtc: end });

const task = (over: Partial<Parameters<typeof deadlineInAbsence>[0]> = {}) => ({
  taskType: TaskType.Task,
  status: TaskStatus.Backlog,
  deadlineUtc: '2026-10-22T15:00:00Z',
  startUtc: null,
  isAllDay: false,
  ...over,
});

// Step 7.5.10b: the card and the details warn when an open task falls due while the person is out of office.
describe('deadlineInAbsence', () => {
  it('warns when the deadline lies inside an absence', () => {
    expect(deadlineInAbsence(task(), [away('2026-10-21T00:00:00Z', '2026-10-24T00:00:00Z')], NOW)).toBe(true);
  });

  it('does not warn for an absence that ends before the deadline or starts after it', () => {
    expect(deadlineInAbsence(task(), [away('2026-10-20T00:00:00Z', '2026-10-22T10:00:00Z')], NOW)).toBe(false);
    expect(deadlineInAbsence(task(), [away('2026-10-23T00:00:00Z', '2026-10-25T00:00:00Z')], NOW)).toBe(false);
  });

  it('does not warn without absences', () => {
    expect(deadlineInAbsence(task(), [], NOW)).toBe(false);
  });

  it('does not warn for finished work, events or a deadline that has already passed', () => {
    const holiday = [away('2026-10-01T00:00:00Z', '2026-12-01T00:00:00Z')];
    expect(deadlineInAbsence(task({ status: TaskStatus.Completed }), holiday, NOW)).toBe(false);
    expect(deadlineInAbsence(task({ status: TaskStatus.Cancelled }), holiday, NOW)).toBe(false);
    expect(deadlineInAbsence(task({ taskType: TaskType.Event }), holiday, NOW)).toBe(false);
    expect(deadlineInAbsence(task({ deadlineUtc: '2026-10-18T12:00:00Z' }), holiday, NOW)).toBe(false);
    expect(deadlineInAbsence(task({ deadlineUtc: null }), holiday, NOW)).toBe(false);
  });

  it('counts a whole-day deadline from the start of its day, so an absence covering part of that day warns', () => {
    const dueDay = task({ deadlineUtc: '2026-10-22T00:00:00Z', isAllDay: true });

    expect(deadlineInAbsence(dueDay, [away('2026-10-22T10:00:00Z', '2026-10-22T14:00:00Z')], NOW)).toBe(true);
    expect(deadlineInAbsence(dueDay, [away('2026-10-23T00:00:00Z', '2026-10-24T00:00:00Z')], NOW)).toBe(false);
  });
});
