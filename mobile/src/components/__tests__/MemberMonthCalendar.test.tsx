import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { MemberMonthCalendar } from '../MemberMonthCalendar';
import { getMemberCalendar } from '../../services/availabilityService';
import {
  BlockKind,
  CalendarSharingLevel,
  DayAvailability,
  LoadLevel,
  MemberStatus,
  type MemberCalendarDto,
  type MemberCalendarEntryDto,
  type MemberTaskDto,
} from '../../types/availability';
import { TaskStatus } from '../../types/task';
import { TeamRole } from '../../types/team';
import { dayKey } from '../../utils/dateUtils';

jest.mock('../../services/availabilityService');

const mockedCalendar = getMemberCalendar as jest.MockedFunction<typeof getMemberCalendar>;

const today = new Date();
const key = dayKey(today);
const isoDay = (d: Date) => `${dayKey(d)}T00:00:00`;

const entry = (over: Partial<MemberCalendarEntryDto>): MemberCalendarEntryDto => ({
  itemId: 'e1', source: 'Team', kind: BlockKind.Busy, isAllDay: false, title: 'Sprint review', location: 'Room 4',
  startUtc: new Date(today.getFullYear(), today.getMonth(), today.getDate(), 9, 0).toISOString(),
  endUtc: new Date(today.getFullYear(), today.getMonth(), today.getDate(), 10, 0).toISOString(),
  ...over,
});

const task = (over: Partial<MemberTaskDto>): MemberTaskDto => ({
  itemId: 't1', title: 'Write report', deadlineUtc: new Date(today.getFullYear(), today.getMonth(), today.getDate(), 12, 0).toISOString(),
  isAllDay: false, effortHours: 3, status: TaskStatus.Backlog, urgencyScore: 0.5, ...over,
});

const calendarOf = (over: Partial<MemberCalendarDto> = {}): MemberCalendarDto => {
  const first = new Date(today.getFullYear(), today.getMonth(), 1);
  const length = new Date(today.getFullYear(), today.getMonth() + 1, 0).getDate();
  return {
    teamId: 't1',
    timeZoneId: null,
    entries: [],
    tasks: [],
    finishedTasks: [],
    member: {
      userId: 'ann', displayName: 'Ann', role: TeamRole.Member, sharingLevel: CalendarSharingLevel.Details,
      currentStatus: MemberStatus.Free, currentWorkingLocation: null, windowLoadPercent: 40, loadLevel: LoadLevel.Light,
      openTasks: 0, tasksWithoutEffort: 0,
      days: Array.from({ length }, (_, i) => ({
        date: isoDay(new Date(first.getFullYear(), first.getMonth(), i + 1)),
        availability: DayAvailability.Free, capacityHours: 8, loadHours: 2, loadPercent: 25, loadLevel: LoadLevel.Light,
      })),
    },
    ...over,
  };
};

describe('MemberMonthCalendar', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedCalendar.mockResolvedValue(calendarOf());
  });

  it('asks for the whole current month, from its first day', async () => {
    await render(<MemberMonthCalendar teamId="t1" userId="ann" />);

    await waitFor(() => expect(mockedCalendar).toHaveBeenCalled());
    const [teamId, userId, days, from] = mockedCalendar.mock.calls[0];
    expect([teamId, userId]).toEqual(['t1', 'ann']);
    expect(days).toBe(new Date(today.getFullYear(), today.getMonth() + 1, 0).getDate());
    expect((from as Date).getDate()).toBe(1);
    expect((from as Date).getMonth()).toBe(today.getMonth());
  });

  it('lists what the person has on the selected day: events, open tasks and finished tasks, with the year in the title', async () => {
    mockedCalendar.mockResolvedValue(calendarOf({
      entries: [entry({})],
      tasks: [task({})],
      finishedTasks: [task({ itemId: 't2', title: 'Old report', status: TaskStatus.Completed })],
    }));
    await render(<MemberMonthCalendar teamId="t1" userId="ann" />);

    await waitFor(() => expect(screen.getByText('Sprint review · Room 4')).toBeTruthy());
    expect(screen.getByText('Write report (3 h)')).toBeTruthy();
    expect(screen.getByText('Old report')).toBeTruthy();
    expect(screen.getByText('done')).toBeTruthy();
    expect(screen.getByText('team')).toBeTruthy();
    expect(screen.getByTestId(`member-month-day-${key}`)).toBeTruthy();
    expect(screen.getAllByText(new RegExp(String(today.getFullYear()))).length).toBeGreaterThan(0);
  });

  it('loads another month when the arrows are used, also into the past', async () => {
    await render(<MemberMonthCalendar teamId="t1" userId="ann" />);
    await waitFor(() => expect(mockedCalendar).toHaveBeenCalledTimes(1));

    await fireEvent.press(screen.getByTestId('month-grid-prev'));

    await waitFor(() => expect(mockedCalendar).toHaveBeenCalledTimes(2));
    const previous = new Date(today.getFullYear(), today.getMonth() - 1, 1);
    const [, , days, from] = mockedCalendar.mock.calls[1];
    expect((from as Date).getMonth()).toBe(previous.getMonth());
    expect(days).toBe(new Date(previous.getFullYear(), previous.getMonth() + 1, 0).getDate());
  });

  it('shows a finished task of an earlier day without a load figure', async () => {
    const past = new Date(today.getFullYear(), today.getMonth(), 1);
    if (dayKey(past) === key) return; // on the first of the month there is no earlier day in this month
    mockedCalendar.mockResolvedValue(calendarOf({
      finishedTasks: [task({
        itemId: 't3', title: 'Filed taxes', status: TaskStatus.Completed,
        deadlineUtc: new Date(past.getFullYear(), past.getMonth(), past.getDate(), 12, 0).toISOString(),
      })],
    }));
    await render(<MemberMonthCalendar teamId="t1" userId="ann" />);
    await waitFor(() => expect(mockedCalendar).toHaveBeenCalled());

    await fireEvent.press(screen.getByTestId(`month-grid-day-${dayKey(past)}`));

    await waitFor(() => expect(screen.getByText('Filed taxes')).toBeTruthy());
    expect(screen.getByText('Free')).toBeTruthy();
    expect(screen.queryByText(/planned of/)).toBeNull();
  });

  it('says so when the day is empty', async () => {
    await render(<MemberMonthCalendar teamId="t1" userId="ann" />);

    await waitFor(() => expect(screen.getByTestId('member-month-empty')).toBeTruthy());
  });

  it('offers a retry when the month cannot be loaded', async () => {
    mockedCalendar.mockRejectedValueOnce({ message: 'Offline' });
    await render(<MemberMonthCalendar teamId="t1" userId="ann" />);

    await waitFor(() => expect(screen.getByText('Offline')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('member-month-retry'));

    await waitFor(() => expect(screen.getByTestId('member-month-empty')).toBeTruthy());
    expect(mockedCalendar).toHaveBeenCalledTimes(2);
  });
});
