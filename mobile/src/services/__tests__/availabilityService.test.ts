import { http } from '../httpClient';
import { getMemberCalendar, getTeamAvailability } from '../availabilityService';
import { updateCalendarSharing } from '../userService';
import { CalendarSharingLevel } from '../../types/availability';

jest.mock('../httpClient', () => ({ http: { get: jest.fn(), put: jest.fn() } }));
jest.mock('../tokenStorage', () => ({ loadSession: jest.fn() }));

const mockedHttp = http as unknown as { get: jest.Mock; put: jest.Mock };

describe('availability services', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedHttp.get.mockResolvedValue({ data: { members: [] } });
    mockedHttp.put.mockResolvedValue({});
  });

  it('asks for the team grid from the given local day, 7 days by default', async () => {
    await getTeamAvailability('team1', 7, new Date(2026, 9, 19, 15, 0));

    expect(mockedHttp.get).toHaveBeenCalledWith('/api/teams/team1/availability', { params: { from: '2026-10-19', days: 7 } });
  });

  it('asks for one member\'s calendar on the member route', async () => {
    await getMemberCalendar('team1', 'user1', 14, new Date(2026, 9, 19));

    expect(mockedHttp.get).toHaveBeenCalledWith('/api/teams/team1/members/user1/calendar', { params: { from: '2026-10-19', days: 14 } });
  });

  it('sends the chosen sharing level', async () => {
    await updateCalendarSharing(CalendarSharingLevel.Details);

    expect(mockedHttp.put).toHaveBeenCalledWith('/api/users/me/calendar-sharing', { level: CalendarSharingLevel.Details });
  });
});
