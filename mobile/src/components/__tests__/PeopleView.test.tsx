import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { PeopleView } from '../PeopleView';
import { getMemberCalendar, getTeamAvailability } from '../../services/availabilityService';
import {
  BlockKind,
  CalendarSharingLevel,
  DayAvailability,
  LoadLevel,
  MemberStatus,
  type DayAvailabilityDto,
  type MemberAvailabilityDto,
} from '../../types/availability';
import { TeamRole, type TeamDto } from '../../types/team';

jest.mock('../../services/availabilityService');
jest.mock('../../services/authService', () => ({ getCurrentUserId: () => 'me' }));

const mockedGrid = getTeamAvailability as jest.MockedFunction<typeof getTeamAvailability>;
const mockedCalendar = getMemberCalendar as jest.MockedFunction<typeof getMemberCalendar>;

const team = (id: string, name: string, isPersonal = false): TeamDto => ({
  id, name, description: null, memberCount: 3, isPersonal, muteNotifications: false, muteChat: false,
});

const days = (first: DayAvailability): DayAvailabilityDto[] =>
  [19, 20, 21].map((d, i) => ({
    date: `2026-10-${d}T00:00:00`,
    availability: i === 0 ? first : DayAvailability.Free,
    capacityHours: 8,
    loadHours: 4,
    loadPercent: 50,
    loadLevel: LoadLevel.Light,
  }));

const person = (userId: string, name: string, over: Partial<MemberAvailabilityDto> = {}): MemberAvailabilityDto => ({
  userId,
  displayName: name,
  role: TeamRole.Member,
  sharingLevel: CalendarSharingLevel.Availability,
  currentStatus: MemberStatus.Free,
  currentWorkingLocation: null,
  windowLoadPercent: 50,
  loadLevel: LoadLevel.Light,
  openTasks: 1,
  tasksWithoutEffort: 0,
  days: days(DayAvailability.Free),
  ...over,
});

const grid = (members: MemberAvailabilityDto[]) => ({ teamId: 't1', from: '2026-10-19T00:00:00', days: 3, nowUtc: '2026-10-19T08:00:00Z', members });

describe('PeopleView', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedGrid.mockResolvedValue(grid([person('me', 'Mia'), person('ann', 'Ann', { currentStatus: MemberStatus.OutOfOffice, windowLoadPercent: 0 })]));
  });

  it('explains itself when the only workspace is the personal one', async () => {
    await render(<PeopleView teams={[team('p', 'Personal', true)]} />);

    expect(screen.getByTestId('people-empty')).toBeTruthy();
    expect(mockedGrid).not.toHaveBeenCalled();
  });

  it('lists everyone with their state right now and their load', async () => {
    await render(<PeopleView teams={[team('t1', 'Core team'), team('p', 'Personal', true)]} />);

    await waitFor(() => expect(screen.getByTestId('people-row-ann')).toBeTruthy());
    expect(mockedGrid).toHaveBeenCalledWith('t1');
    expect(screen.getByText('Mia (you)')).toBeTruthy();
    expect(screen.getByText('Out of office')).toBeTruthy();
    expect(screen.getByText('Core team')).toBeTruthy();
    expect(screen.getAllByText('Load 50%').length).toBeGreaterThan(0);
    expect(screen.getByTestId('people-cell-ann-2026-10-19')).toBeTruthy();
  });

  it('switches the team when there are several', async () => {
    await render(<PeopleView teams={[team('t1', 'Core team'), team('t2', 'Side team')]} />);
    await waitFor(() => expect(mockedGrid).toHaveBeenCalledWith('t1'));

    await fireEvent.press(screen.getByTestId('people-team-t2'));

    await waitFor(() => expect(mockedGrid).toHaveBeenCalledWith('t2'));
  });

  it('opens one person\'s calendar: the titles they share, the kind of block when they do not, and what is due', async () => {
    mockedCalendar.mockResolvedValue({
      teamId: 't1',
      timeZoneId: 'Europe/Warsaw',
      member: person('ann', 'Ann', { sharingLevel: CalendarSharingLevel.Availability }),
      entries: [
        { itemId: 'e1', source: 'Personal', kind: BlockKind.Busy, startUtc: new Date(2026, 9, 19, 10, 0).toISOString(), endUtc: new Date(2026, 9, 19, 11, 0).toISOString(), isAllDay: false, title: null, location: null },
        { itemId: 'e2', source: 'Team', kind: BlockKind.Busy, startUtc: new Date(2026, 9, 20, 9, 0).toISOString(), endUtc: new Date(2026, 9, 20, 10, 0).toISOString(), isAllDay: false, title: 'Sprint review', location: 'Room 4' },
      ],
      tasks: [{ itemId: 't1', title: 'Write report', deadlineUtc: '2026-10-21T00:00:00Z', isAllDay: true, effortHours: 3, status: 0, urgencyScore: 0.5 }],
    });
    await render(<PeopleView teams={[team('t1', 'Core team')]} />);
    await waitFor(() => expect(screen.getByTestId('people-row-ann')).toBeTruthy());

    await fireEvent.press(screen.getByTestId('people-row-ann'));

    await waitFor(() => expect(screen.getByTestId('member-calendar')).toBeTruthy());
    expect(mockedCalendar).toHaveBeenCalledWith('t1', 'ann');
    await waitFor(() => expect(screen.getByText('Sprint review · Room 4')).toBeTruthy());
    expect(screen.getAllByText('Busy').length).toBeGreaterThan(0); // the hidden block shows only its kind
    expect(screen.getByText('Write report (3 h)')).toBeTruthy();
    expect(screen.getByText('Shares: availability only.')).toBeTruthy();

    await fireEvent.press(screen.getByTestId('member-calendar-back'));
    expect(screen.getByTestId('people-row-ann')).toBeTruthy();
  });

  it('says when a person shares nothing, and when load is understated because tasks have no estimate', async () => {
    mockedCalendar.mockResolvedValue({
      teamId: 't1', timeZoneId: null, entries: [], tasks: [],
      member: person('ann', 'Ann', { sharingLevel: CalendarSharingLevel.Off, tasksWithoutEffort: 2 }),
    });
    mockedGrid.mockResolvedValue(grid([person('ann', 'Ann', { sharingLevel: CalendarSharingLevel.Off, tasksWithoutEffort: 2 })]));
    await render(<PeopleView teams={[team('t1', 'Core team')]} />);
    await waitFor(() => expect(screen.getByTestId('people-row-ann')).toBeTruthy());

    await fireEvent.press(screen.getByTestId('people-row-ann'));

    await waitFor(() => expect(screen.getByText('Ann does not share their calendar - only team items are listed.')).toBeTruthy());
    expect(screen.getByText('2 open tasks have no effort estimate, so the load is higher than shown.')).toBeTruthy();
  });

  it('offers a retry when the grid cannot be loaded', async () => {
    mockedGrid.mockRejectedValueOnce({ message: 'Offline' });
    await render(<PeopleView teams={[team('t1', 'Core team')]} />);

    await waitFor(() => expect(screen.getByText('Offline')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('people-retry'));

    await waitFor(() => expect(screen.getByTestId('people-row-ann')).toBeTruthy());
  });
});
