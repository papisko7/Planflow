import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import NotificationsScreen from '../NotificationsScreen';
import { getInbox, markAllNotificationsRead, markNotificationRead } from '../../services/notificationService';
import { NotificationEvent, type InboxNotification } from '../../types/notification';

jest.mock('../../services/notificationService');

const mockNavigate = jest.fn();
const mockSetOptions = jest.fn();
jest.mock('@react-navigation/native', () => {
  const React = require('react');
  return {
    useNavigation: () => ({ navigate: mockNavigate, setOptions: mockSetOptions }),
    useFocusEffect: (effect: () => void) => React.useEffect(effect, [effect]),
  };
});

const mockedInbox = getInbox as jest.MockedFunction<typeof getInbox>;
const mockedRead = markNotificationRead as jest.MockedFunction<typeof markNotificationRead>;
const mockedReadAll = markAllNotificationsRead as jest.MockedFunction<typeof markAllNotificationsRead>;

const entry = (over: Partial<InboxNotification> = {}): InboxNotification => ({
  id: 'n1',
  event: NotificationEvent.TaskAssignedToYou,
  title: 'New task for you: Prepare the demo',
  body: 'Ann assigned the task to you.',
  createdAtUtc: new Date().toISOString(),
  isRead: false,
  teamId: 'team-1',
  taskId: 'task-1',
  ...over,
});

describe('NotificationsScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedRead.mockResolvedValue(undefined);
    mockedReadAll.mockResolvedValue(undefined);
  });

  it('lists the entries and marks the unread ones', async () => {
    mockedInbox.mockResolvedValue([entry(), entry({ id: 'n2', title: 'Older one', isRead: true })]);

    await render(<NotificationsScreen />);

    await waitFor(() => expect(screen.getByText('New task for you: Prepare the demo')).toBeTruthy());
    expect(screen.getAllByText('Ann assigned the task to you.')).toHaveLength(2);
    expect(screen.getByTestId('notification-unread-n1')).toBeTruthy();
    expect(screen.queryByTestId('notification-unread-n2')).toBeNull();
  });

  it('tapping an entry marks it read and opens the task it is about', async () => {
    mockedInbox.mockResolvedValue([entry()]);
    await render(<NotificationsScreen />);
    await waitFor(() => screen.getByTestId('notification-n1'));

    await fireEvent.press(screen.getByTestId('notification-n1'));

    expect(mockedRead).toHaveBeenCalledWith('n1');
    expect(mockNavigate).toHaveBeenCalledWith('TaskDetail', { taskId: 'task-1', teamId: 'team-1' });
    expect(screen.queryByTestId('notification-unread-n1')).toBeNull();
  });

  it('an entry without a task is marked read but goes nowhere', async () => {
    mockedInbox.mockResolvedValue([entry({ taskId: null, event: NotificationEvent.AddedToTeam })]);
    await render(<NotificationsScreen />);
    await waitFor(() => screen.getByTestId('notification-n1'));

    await fireEvent.press(screen.getByTestId('notification-n1'));

    expect(mockedRead).toHaveBeenCalledWith('n1');
    expect(mockNavigate).not.toHaveBeenCalled();
  });

  it('shows a friendly empty state', async () => {
    mockedInbox.mockResolvedValue([]);

    await render(<NotificationsScreen />);

    await waitFor(() => expect(screen.getByText('No notifications yet')).toBeTruthy());
  });

  it('shows the error instead of an endless spinner when the server is unreachable', async () => {
    mockedInbox.mockRejectedValue({ status: null, message: 'Network error', isNetworkError: true });

    await render(<NotificationsScreen />);

    await waitFor(() => expect(screen.getByText("Couldn't load notifications")).toBeTruthy());
    expect(screen.getByText('Network error')).toBeTruthy();
  });

  it('offers "Mark all read" only while something is unread, and clears every dot at once', async () => {
    mockedInbox.mockResolvedValue([entry(), entry({ id: 'n2' })]);
    await render(<NotificationsScreen />);
    await waitFor(() => screen.getByTestId('notification-n1'));

    // The screen puts the button into the navigation header; render it the way React Navigation would.
    const lastOptions = () => mockSetOptions.mock.calls[mockSetOptions.mock.calls.length - 1][0];
    const header = await render(lastOptions().headerRight());
    await fireEvent.press(header.getByTestId('mark-all-read'));

    expect(mockedReadAll).toHaveBeenCalled();
    await waitFor(() => expect(screen.queryByTestId('notification-unread-n1')).toBeNull());
    expect(screen.queryByTestId('notification-unread-n2')).toBeNull();
    expect(lastOptions().headerRight()).toBeNull();
  });

  it('loads older entries when the list is scrolled to the end', async () => {
    const first = Array.from({ length: 30 }, (_, i) => entry({ id: `n${i}`, title: `Entry ${i}`, createdAtUtc: new Date(Date.now() - i * 60_000).toISOString() }));
    mockedInbox.mockResolvedValueOnce(first).mockResolvedValueOnce([entry({ id: 'older', title: 'Older entry' })]);
    await render(<NotificationsScreen />);
    await waitFor(() => screen.getByTestId('notifications-list'));

    await fireEvent(screen.getByTestId('notifications-list'), 'endReached');

    await waitFor(() => expect(mockedInbox).toHaveBeenCalledTimes(2));
    expect(mockedInbox.mock.calls[1][0]).toBe(first[29].createdAtUtc); // asks for entries older than the last one it has
  });
});
