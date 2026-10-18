import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import * as WebBrowser from 'expo-web-browser';
import { GoogleCalendarBanner } from '../GoogleCalendarBanner';
import { getGoogleCalendarStatus, getGoogleConnectUrl, syncGoogleCalendarNow, type GoogleCalendarStatus } from '../../services/calendarService';

jest.mock('../../services/calendarService');
jest.mock('expo-web-browser', () => ({ openBrowserAsync: jest.fn().mockResolvedValue({ type: 'dismiss' }) }));

const mockedStatus = getGoogleCalendarStatus as jest.MockedFunction<typeof getGoogleCalendarStatus>;
const mockedConnectUrl = getGoogleConnectUrl as jest.MockedFunction<typeof getGoogleConnectUrl>;
const mockedSync = syncGoogleCalendarNow as jest.MockedFunction<typeof syncGoogleCalendarNow>;

const status = (over: Partial<GoogleCalendarStatus> = {}): GoogleCalendarStatus => ({
  connected: true,
  email: 'kacper@example.com',
  lastSyncedAtUtc: '2026-10-18T10:00:00.000Z',
  tasksConnected: true,
  lastTasksSyncedAtUtc: null,
  ...over,
});

// Step 7.5.3: an account linked before Google Tasks existed has no tasks permission, so the banner offers a one-tap reconnect.
describe('GoogleCalendarBanner - Google Tasks', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedSync.mockResolvedValue(undefined);
  });

  it('with the tasks permission it says Tasks are connected and offers no reconnect', async () => {
    mockedStatus.mockResolvedValue(status({ lastTasksSyncedAtUtc: null }));

    await render(<GoogleCalendarBanner onSynced={jest.fn()} />);

    await waitFor(() => expect(screen.getByTestId('google-tasks-status')).toBeTruthy());
    expect(screen.getByText(/waiting for the first sync/)).toBeTruthy();
    expect(screen.queryByTestId('google-tasks-reconnect')).toBeNull();
  });

  it('shows when Google Tasks were last synced', async () => {
    const synced = '2026-10-18T11:30:00.000Z';
    mockedStatus.mockResolvedValue(status({ lastTasksSyncedAtUtc: synced }));

    await render(<GoogleCalendarBanner onSynced={jest.fn()} />);

    await waitFor(() => expect(screen.getByTestId('google-tasks-status').props.children.join('')).toContain(new Date(synced).toLocaleString()));
  });

  it('without the tasks permission it explains why and offers to reconnect', async () => {
    mockedStatus.mockResolvedValue(status({ tasksConnected: false }));

    await render(<GoogleCalendarBanner onSynced={jest.fn()} />);

    await waitFor(() => expect(screen.getByTestId('google-tasks-reconnect-notice')).toBeTruthy());
    expect(screen.getByTestId('google-tasks-reconnect')).toBeTruthy();
    expect(screen.queryByTestId('google-tasks-status')).toBeNull();
    expect(screen.getByText(/Google Calendar:/)).toBeTruthy(); // the calendar itself is still shown as connected
  });

  describe('reconnecting', () => {
    // The banner waits 3 s for the background job to settle before reloading, so this test really waits for it.
    it('opens the Google consent page, then syncs right away so the tasks appear without a manual refresh', async () => {
      mockedStatus
        .mockResolvedValueOnce(status({ tasksConnected: false })) // when the banner opens
        .mockResolvedValue(status({ tasksConnected: true })); // after the user came back from Google
      mockedConnectUrl.mockResolvedValue('https://accounts.google.com/consent');
      const onSynced = jest.fn();
      await render(<GoogleCalendarBanner onSynced={onSynced} />);
      await waitFor(() => expect(screen.getByTestId('google-tasks-reconnect')).toBeTruthy());

      await fireEvent.press(screen.getByTestId('google-tasks-reconnect'));
      await waitFor(() => expect(WebBrowser.openBrowserAsync).toHaveBeenCalledWith('https://accounts.google.com/consent'));

      await waitFor(() => expect(onSynced).toHaveBeenCalled(), { timeout: 8000 });
      expect(mockedSync).toHaveBeenCalledTimes(1);
      await waitFor(() => expect(screen.queryByTestId('google-tasks-reconnect')).toBeNull());
      expect(screen.getByTestId('google-tasks-status')).toBeTruthy();
      await waitFor(() => expect(screen.queryByTestId('google-banner-busy')).toBeNull()); // let the banner finish before the test ends
    }, 20000);
  });
});
