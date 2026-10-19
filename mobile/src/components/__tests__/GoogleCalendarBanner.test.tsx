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
  syncing: false,
  ...over,
});

// An account linked before Google Tasks existed has no tasks permission, so the banner offers a one-tap reconnect.
describe('GoogleCalendarBanner - Google Tasks', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedSync.mockResolvedValue({ started: true });
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

  // Google is fetched only on the button; the banner follows the server's `syncing` flag until the sync is over.
  describe('Sync now', () => {
    const SYNC_NOW = 'Sync now';

    it('shows "Syncing…" while the server is syncing, then reloads the items once it is done', async () => {
      mockedStatus
        .mockResolvedValueOnce(status()) // when the banner opens
        .mockResolvedValueOnce(status({ syncing: true })) // first look after the press: still going
        .mockResolvedValue(status({ lastSyncedAtUtc: '2026-10-19T09:00:00.000Z' })); // finished
      const onSynced = jest.fn();
      await render(<GoogleCalendarBanner onSynced={onSynced} />);
      await waitFor(() => expect(screen.getByText(SYNC_NOW)).toBeTruthy());

      const pressed = fireEvent.press(screen.getByText(SYNC_NOW)); // resolves only when the whole sync has been followed to its end

      await waitFor(() => expect(screen.getByText('Syncing…')).toBeTruthy());
      expect(onSynced).not.toHaveBeenCalled();
      await pressed;
      expect(onSynced).toHaveBeenCalledTimes(1);
      expect(mockedSync).toHaveBeenCalledTimes(1);
      await waitFor(() => expect(screen.getByText(SYNC_NOW)).toBeTruthy());
      await waitFor(() => expect(screen.queryByTestId('google-banner-busy')).toBeNull());
    }, 20000);

    it('says so and does not wait when the last sync was moments ago', async () => {
      mockedStatus.mockResolvedValue(status());
      mockedSync.mockResolvedValue({ started: false });
      const onSynced = jest.fn();
      await render(<GoogleCalendarBanner onSynced={onSynced} />);
      await waitFor(() => expect(screen.getByText(SYNC_NOW)).toBeTruthy());

      await fireEvent.press(screen.getByText(SYNC_NOW));

      await waitFor(() => expect(screen.getByText(/Synced a moment ago/)).toBeTruthy());
      expect(onSynced).not.toHaveBeenCalled();
      expect(mockedStatus).toHaveBeenCalledTimes(1); // only the one when the banner opened
      await waitFor(() => expect(screen.queryByTestId('google-banner-busy')).toBeNull());
    });

    it('picks the wait up again when opened while a sync is still running', async () => {
      mockedStatus
        .mockResolvedValueOnce(status({ syncing: true })) // when the banner opens
        .mockResolvedValue(status()); // by the next look it is done
      const onSynced = jest.fn();

      await render(<GoogleCalendarBanner onSynced={onSynced} />);

      await waitFor(() => expect(screen.getByText('Syncing…')).toBeTruthy());
      expect(mockedSync).not.toHaveBeenCalled(); // nothing is started by merely opening the screen
      await waitFor(() => expect(onSynced).toHaveBeenCalledTimes(1), { timeout: 8000 });
      await waitFor(() => expect(screen.getByText(SYNC_NOW)).toBeTruthy());
      await waitFor(() => expect(screen.queryByTestId('google-banner-busy')).toBeNull());
    }, 20000);
  });

  describe('reconnecting', () => {
    // After reconnecting the banner syncs once, then follows it to the end, so this test really waits for the first poll.
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
