import {
  BlockKind,
  CalendarSharingLevel,
  DayAvailability,
  LoadLevel,
  MemberStatus,
  type MemberAvailabilityDto,
  type MemberCalendarEntryDto,
  type MemberTaskDto,
} from '../../types/availability';
import { TaskStatus } from '../../types/task';
import { TeamRole } from '../../types/team';
import {
  assigneeWarning,
  entryOnDay,
  entryTitle,
  formatLoad,
  hoursLabel,
  monthWindow,
  parseApiDay,
  pickerHint,
  taskDueOnDay,
} from '../availability';

const member = (over: Partial<MemberAvailabilityDto> = {}): MemberAvailabilityDto => ({
  userId: 'u1',
  displayName: 'Anna',
  role: TeamRole.Member,
  sharingLevel: CalendarSharingLevel.Availability,
  currentStatus: MemberStatus.Free,
  currentWorkingLocation: null,
  windowLoadPercent: null,
  loadLevel: LoadLevel.Light,
  openTasks: 0,
  tasksWithoutEffort: 0,
  days: [],
  ...over,
});

const entry = (over: Partial<MemberCalendarEntryDto>): MemberCalendarEntryDto => ({
  itemId: 'e1',
  source: 'Personal',
  kind: BlockKind.Busy,
  startUtc: '2026-10-20T10:00:00Z',
  endUtc: '2026-10-20T11:00:00Z',
  isAllDay: false,
  title: null,
  location: null,
  ...over,
});

describe('formatLoad', () => {
  it('rounds a percentage, shows a dash for "nothing", and says "no time" for work with no capacity', () => {
    expect(formatLoad(null)).toBe('-');
    expect(formatLoad(62.4)).toBe('62%');
    expect(formatLoad(999)).toBe('no time');
  });
});

describe('parseApiDay', () => {
  it('reads the date part as a local day, whatever follows it', () => {
    const day = parseApiDay('2026-10-19T00:00:00');

    expect([day.getFullYear(), day.getMonth(), day.getDate()]).toEqual([2026, 9, 19]);
  });
});

describe('pickerHint', () => {
  it('is empty when nothing is known', () => {
    expect(pickerHint(undefined)).toBeNull();
    expect(pickerHint(member())).toBeNull();
  });

  it('combines the state right now with the load of the week', () => {
    expect(pickerHint(member({ currentStatus: MemberStatus.OutOfOffice, windowLoadPercent: 40 }))).toBe('away · 40%');
    expect(pickerHint(member({ currentStatus: MemberStatus.Busy }))).toBe('busy');
    expect(pickerHint(member({ currentStatus: MemberStatus.FocusTime, windowLoadPercent: 120 }))).toBe('focus · 120%');
    expect(pickerHint(member({ windowLoadPercent: 75.4 }))).toBe('75%');
  });
});

describe('assigneeWarning', () => {
  it('warns about a person who is away right now', () => {
    expect(assigneeWarning(member({ currentStatus: MemberStatus.OutOfOffice }))).toBe('Anna is out of office right now.');
  });

  it('warns about an overloaded person, with the load', () => {
    const text = assigneeWarning(member({ loadLevel: LoadLevel.Overloaded, windowLoadPercent: 130 }));

    expect(text).toContain('overloaded');
    expect(text).toContain('130%');
  });

  it('stays quiet for a person who is merely busy or has a balanced load', () => {
    expect(assigneeWarning(member({ currentStatus: MemberStatus.Busy, loadLevel: LoadLevel.Balanced }))).toBeNull();
    expect(assigneeWarning(undefined)).toBeNull();
  });
});

describe('entryOnDay', () => {
  it('reads an all-day block as literal dates (the end is the day after the last day)', () => {
    const holiday = entry({ isAllDay: true, startUtc: '2026-10-20T00:00:00Z', endUtc: '2026-10-22T00:00:00Z' });

    expect(entryOnDay(holiday, new Date(2026, 9, 19))).toBe(false);
    expect(entryOnDay(holiday, new Date(2026, 9, 20))).toBe(true);
    expect(entryOnDay(holiday, new Date(2026, 9, 21))).toBe(true);
    expect(entryOnDay(holiday, new Date(2026, 9, 22))).toBe(false);
  });

  it('places a timed block on the days it touches by the local clock', () => {
    const meeting = entry({ startUtc: new Date(2026, 9, 20, 10, 0).toISOString(), endUtc: new Date(2026, 9, 20, 11, 0).toISOString() });

    expect(entryOnDay(meeting, new Date(2026, 9, 20))).toBe(true);
    expect(entryOnDay(meeting, new Date(2026, 9, 21))).toBe(false);
  });
});

describe('taskDueOnDay', () => {
  const task = (over: Partial<MemberTaskDto>): MemberTaskDto => ({
    itemId: 't1', title: 'Report', deadlineUtc: '2026-10-20T00:00:00Z', isAllDay: true, effortHours: 2, status: TaskStatus.Backlog, urgencyScore: 0.5, ...over,
  });

  it('puts an all-day deadline on the date it stores, wherever the viewer is', () => {
    expect(taskDueOnDay(task({}), new Date(2026, 9, 20))).toBe(true);
    expect(taskDueOnDay(task({}), new Date(2026, 9, 19))).toBe(false);
  });

  it('puts a timed deadline on the local day of that moment', () => {
    const due = new Date(2026, 9, 20, 15, 0).toISOString();

    expect(taskDueOnDay(task({ isAllDay: false, deadlineUtc: due }), new Date(2026, 9, 20))).toBe(true);
  });
});

describe('labels', () => {
  it('shows the title when it is shared and the kind of block when it is not', () => {
    expect(entryTitle(entry({ title: 'Therapy' }))).toBe('Therapy');
    expect(entryTitle(entry({ kind: BlockKind.OutOfOffice }))).toBe('Out of office');
    expect(entryTitle(entry({}))).toBe('Busy');
  });

  it('writes effort in hours or minutes, or says there is no estimate', () => {
    expect(hoursLabel(null)).toBe('no estimate');
    expect(hoursLabel(2)).toBe('2 h');
    expect(hoursLabel(0.5)).toBe('30 min');
  });
});

describe('monthWindow', () => {
  it('spans the whole month from its first day, leap February included', () => {
    expect(monthWindow(new Date(2026, 9, 19))).toEqual({ from: new Date(2026, 9, 1), days: 31 });
    expect(monthWindow(new Date(2026, 1, 10)).days).toBe(28);
    expect(monthWindow(new Date(2028, 1, 10)).days).toBe(29);
  });
});

describe('DayAvailability', () => {
  it('has the values the server sends', () => {
    expect(DayAvailability.OutOfOffice).toBe(4);
    expect(MemberStatus.NoData).toBe(0);
  });
});
