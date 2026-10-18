import { Alert, type AlertButton } from 'react-native';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import NotificationsScreen from '../NotificationsScreen';
import {
  clearNotifications,
  deleteNotification,
  deleteNotifications,
  getInbox,
  markAllNotificationsRead,
  markNotificationRead,
  markNotificationsRead,
} from '../../services/notificationService';
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

// The real row reacts to a finger drag (PanResponder); here a swipe is a button, so the screen's reaction to it can be tested.
jest.mock('../../components/SwipeableRow', () => {
  const React = require('react');
  const { Pressable, View } = require('react-native');
  return {
    SwipeableRow: ({ children, onSwipeRight, onSwipeLeft }: { children: unknown; onSwipeRight?: () => void; onSwipeLeft?: () => void }) =>
      React.createElement(
        View,
        null,
        onSwipeRight ? React.createElement(Pressable, { testID: 'swipe-right', onPress: onSwipeRight }) : null,
        onSwipeLeft ? React.createElement(Pressable, { testID: 'swipe-left', onPress: onSwipeLeft }) : null,
        children,
      ),
  };
});

const mockedInbox = getInbox as jest.MockedFunction<typeof getInbox>;
const mockedRead = markNotificationRead as jest.MockedFunction<typeof markNotificationRead>;
const mockedReadAll = markAllNotificationsRead as jest.MockedFunction<typeof markAllNotificationsRead>;
const mockedReadMany = markNotificationsRead as jest.MockedFunction<typeof markNotificationsRead>;
const mockedDelete = deleteNotification as jest.MockedFunction<typeof deleteNotification>;
const mockedDeleteMany = deleteNotifications as jest.MockedFunction<typeof deleteNotifications>;
const mockedClear = clearNotifications as jest.MockedFunction<typeof clearNotifications>;

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

// The screen puts its buttons into the navigation header; render them the way React Navigation would.
const lastOptions = () => mockSetOptions.mock.calls[mockSetOptions.mock.calls.length - 1][0];

// Alert.alert is a spy; pressing one of its buttons runs that button's handler (inside act, since it updates state).
let alertSpy: jest.SpyInstance;
const pressAlertButton = async (text: string) => {
  const buttons = alertSpy.mock.calls[alertSpy.mock.calls.length - 1][2] as AlertButton[];
  await act(async () => {
    await buttons.find((b) => b.text === text)?.onPress?.();
  });
};

describe('NotificationsScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedRead.mockResolvedValue(undefined);
    mockedReadAll.mockResolvedValue(undefined);
    mockedReadMany.mockResolvedValue(undefined);
    mockedDelete.mockResolvedValue(undefined);
    mockedDeleteMany.mockResolvedValue(undefined);
    mockedClear.mockResolvedValue(undefined);
    alertSpy = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);
  });

  afterEach(() => alertSpy.mockRestore());

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

    // A single tap waits a moment in case a second one follows (that would tick the entry instead).
    await waitFor(() => expect(mockNavigate).toHaveBeenCalledWith('TaskDetail', { taskId: 'task-1', teamId: 'team-1' }));
    expect(mockedRead).toHaveBeenCalledWith('n1');
    expect(screen.queryByTestId('notification-unread-n1')).toBeNull();
  });

  it('a quick double tap ticks the entry and starts the selection mode instead of opening it', async () => {
    mockedInbox.mockResolvedValue([entry(), entry({ id: 'n2' })]);
    await render(<NotificationsScreen />);
    await waitFor(() => screen.getByTestId('notification-n1'));

    await fireEvent.press(screen.getByTestId('notification-n1'));
    await fireEvent.press(screen.getByTestId('notification-n1'));
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 400)); // longer than the double-tap window
    });

    expect(screen.getByTestId('selection-bar')).toBeTruthy();
    expect(screen.getByTestId('notification-ticked-n1')).toBeTruthy();
    expect(screen.queryByTestId('notification-ticked-n2')).toBeNull();
    expect(lastOptions().title).toBe('1 selected');
    expect(mockNavigate).not.toHaveBeenCalled();
    expect(mockedRead).not.toHaveBeenCalled();
  });

  it('two taps on different entries open the first one and do not tick anything', async () => {
    mockedInbox.mockResolvedValue([entry(), entry({ id: 'n2', taskId: 'task-2' })]);
    await render(<NotificationsScreen />);
    await waitFor(() => screen.getByTestId('notification-n1'));

    await fireEvent.press(screen.getByTestId('notification-n1'));
    await fireEvent.press(screen.getByTestId('notification-n2'));

    expect(mockNavigate).toHaveBeenCalledWith('TaskDetail', { taskId: 'task-1', teamId: 'team-1' });
    await waitFor(() => expect(mockNavigate).toHaveBeenCalledWith('TaskDetail', { taskId: 'task-2', teamId: 'team-1' }));
    expect(screen.queryByTestId('selection-bar')).toBeNull();
  });

  it('an entry without a task is marked read but goes nowhere', async () => {
    mockedInbox.mockResolvedValue([entry({ taskId: null, event: NotificationEvent.AddedToTeam })]);
    await render(<NotificationsScreen />);
    await waitFor(() => screen.getByTestId('notification-n1'));

    await fireEvent.press(screen.getByTestId('notification-n1'));

    await waitFor(() => expect(mockedRead).toHaveBeenCalledWith('n1'));
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
    const view = await render(<NotificationsScreen />);
    await waitFor(() => view.getByTestId('notification-n1'));

    const header = await render(lastOptions().headerRight()); // (a second render becomes `screen`, so the list is queried through `view`)
    await fireEvent.press(header.getByTestId('mark-all-read'));

    expect(mockedReadAll).toHaveBeenCalled();
    await waitFor(() => expect(view.queryByTestId('notification-unread-n1')).toBeNull());
    expect(view.queryByTestId('notification-unread-n2')).toBeNull();

    const afterwards = await render(lastOptions().headerRight());
    expect(afterwards.queryByTestId('mark-all-read')).toBeNull();
    expect(afterwards.getByTestId('clear-all')).toBeTruthy(); // clearing stays possible when everything is read
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

  // ---------- swipes ----------

  it('swiping a row right deletes that entry and leaves the others', async () => {
    mockedInbox.mockResolvedValue([entry(), entry({ id: 'n2', title: 'Second' })]);
    await render(<NotificationsScreen />);
    await waitFor(() => screen.getByTestId('notification-n1'));

    await fireEvent.press(screen.getAllByTestId('swipe-right')[0]);

    expect(mockedDelete).toHaveBeenCalledWith('n1');
    expect(screen.queryByTestId('notification-n1')).toBeNull();
    expect(screen.getByTestId('notification-n2')).toBeTruthy();
  });

  it('shows the truth again when the server refuses a delete', async () => {
    mockedInbox.mockResolvedValue([entry()]);
    mockedDelete.mockRejectedValue(new Error('offline'));
    await render(<NotificationsScreen />);
    await waitFor(() => screen.getByTestId('notification-n1'));

    await fireEvent.press(screen.getByTestId('swipe-right'));

    await waitFor(() => expect(mockedInbox).toHaveBeenCalledTimes(2)); // reloaded
    await waitFor(() => expect(screen.getByTestId('notification-n1')).toBeTruthy());
  });

  it('swiping an unread row left marks it read; a read row has no such swipe', async () => {
    mockedInbox.mockResolvedValue([entry(), entry({ id: 'n2', isRead: true })]);
    await render(<NotificationsScreen />);
    await waitFor(() => screen.getByTestId('notification-n1'));

    expect(screen.getAllByTestId('swipe-left')).toHaveLength(1); // only the unread one
    await fireEvent.press(screen.getByTestId('swipe-left'));

    expect(mockedRead).toHaveBeenCalledWith('n1');
    expect(screen.queryByTestId('notification-unread-n1')).toBeNull();
    expect(mockNavigate).not.toHaveBeenCalled(); // marking read does not open the task
  });

  // ---------- ticking several ----------

  it('long-press starts ticking: the title counts them, taps tick more, swipes are off and tapping does not open the task', async () => {
    mockedInbox.mockResolvedValue([entry(), entry({ id: 'n2' }), entry({ id: 'n3' })]);
    await render(<NotificationsScreen />);
    await waitFor(() => screen.getByTestId('notification-n1'));
    expect(screen.queryByTestId('selection-bar')).toBeNull();

    await fireEvent(screen.getByTestId('notification-n1'), 'longPress');
    await fireEvent.press(screen.getByTestId('notification-n2'));

    expect(screen.getByTestId('selection-bar')).toBeTruthy();
    expect(screen.getByTestId('notification-ticked-n1')).toBeTruthy();
    expect(screen.getByTestId('notification-ticked-n2')).toBeTruthy();
    expect(screen.queryByTestId('notification-ticked-n3')).toBeNull();
    expect(lastOptions().title).toBe('2 selected');
    expect(screen.queryByTestId('swipe-right')).toBeNull();
    expect(mockNavigate).not.toHaveBeenCalled();
    expect(mockedRead).not.toHaveBeenCalled();
  });

  it('unticking the last entry leaves the ticking mode', async () => {
    mockedInbox.mockResolvedValue([entry()]);
    await render(<NotificationsScreen />);
    await waitFor(() => screen.getByTestId('notification-n1'));
    await fireEvent(screen.getByTestId('notification-n1'), 'longPress');
    expect(screen.getByTestId('selection-bar')).toBeTruthy();

    await fireEvent.press(screen.getByTestId('notification-n1'));

    expect(screen.queryByTestId('selection-bar')).toBeNull();
    expect(lastOptions().title).toBe('Notifications');
  });

  it('the close button in the header leaves the ticking mode', async () => {
    mockedInbox.mockResolvedValue([entry()]);
    const view = await render(<NotificationsScreen />);
    await waitFor(() => view.getByTestId('notification-n1'));
    await fireEvent(view.getByTestId('notification-n1'), 'longPress');
    expect(view.getByTestId('selection-bar')).toBeTruthy();

    const left = await render(lastOptions().headerLeft());
    await fireEvent.press(left.getByTestId('selection-cancel'));

    expect(view.queryByTestId('selection-bar')).toBeNull();
  });

  it('"Select all" ticks everything that is loaded', async () => {
    mockedInbox.mockResolvedValue([entry(), entry({ id: 'n2' }), entry({ id: 'n3' })]);
    await render(<NotificationsScreen />);
    await waitFor(() => screen.getByTestId('notification-n1'));
    await fireEvent(screen.getByTestId('notification-n1'), 'longPress');

    await fireEvent.press(screen.getByTestId('selection-select-all'));

    expect(lastOptions().title).toBe('3 selected');
  });

  it('"Mark read" in the bar reads exactly the ticked entries with one request', async () => {
    mockedInbox.mockResolvedValue([entry(), entry({ id: 'n2' }), entry({ id: 'n3' })]);
    await render(<NotificationsScreen />);
    await waitFor(() => screen.getByTestId('notification-n1'));
    await fireEvent(screen.getByTestId('notification-n1'), 'longPress');
    await fireEvent.press(screen.getByTestId('notification-n3'));

    await fireEvent.press(screen.getByTestId('selection-mark-read'));

    expect(mockedReadMany).toHaveBeenCalledWith(['n1', 'n3']);
    expect(screen.queryByTestId('notification-unread-n1')).toBeNull();
    expect(screen.getByTestId('notification-unread-n2')).toBeTruthy(); // not ticked: still unread
    expect(screen.queryByTestId('selection-bar')).toBeNull();
  });

  it('"Delete" in the bar asks first, then removes exactly the ticked entries with one request', async () => {
    mockedInbox.mockResolvedValue([entry(), entry({ id: 'n2' }), entry({ id: 'n3' })]);
    await render(<NotificationsScreen />);
    await waitFor(() => screen.getByTestId('notification-n1'));
    await fireEvent(screen.getByTestId('notification-n1'), 'longPress');
    await fireEvent.press(screen.getByTestId('notification-n2'));

    await fireEvent.press(screen.getByTestId('selection-delete'));
    expect(alertSpy.mock.calls[0][0]).toBe('Delete 2 notifications?');
    expect(mockedDeleteMany).not.toHaveBeenCalled(); // nothing happens until the user confirms

    await pressAlertButton('Delete');

    expect(mockedDeleteMany).toHaveBeenCalledWith(['n1', 'n2']);
    expect(screen.queryByTestId('notification-n1')).toBeNull();
    expect(screen.queryByTestId('notification-n2')).toBeNull();
    expect(screen.getByTestId('notification-n3')).toBeTruthy();
    expect(screen.queryByTestId('selection-bar')).toBeNull();
  });

  it('cancelling the delete question keeps everything', async () => {
    mockedInbox.mockResolvedValue([entry()]);
    await render(<NotificationsScreen />);
    await waitFor(() => screen.getByTestId('notification-n1'));
    await fireEvent(screen.getByTestId('notification-n1'), 'longPress');
    await fireEvent.press(screen.getByTestId('selection-delete'));

    await pressAlertButton('Cancel');

    expect(mockedDeleteMany).not.toHaveBeenCalled();
    expect(screen.getByTestId('notification-n1')).toBeTruthy();
  });

  // ---------- clear all ----------

  it('"Clear all" asks first, empties the server inbox and then reloads what is left', async () => {
    mockedInbox.mockResolvedValueOnce([entry(), entry({ id: 'n2' })]).mockResolvedValue([]);
    const view = await render(<NotificationsScreen />);
    await waitFor(() => view.getByTestId('notification-n1'));

    const header = await render(lastOptions().headerRight());
    await fireEvent.press(header.getByTestId('clear-all'));
    expect(alertSpy.mock.calls[0][0]).toBe('Clear all notifications?');
    expect(mockedClear).not.toHaveBeenCalled();

    await pressAlertButton('Clear all');

    expect(mockedClear).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(view.getByText('No notifications yet')).toBeTruthy());
    expect(mockedInbox).toHaveBeenCalledTimes(2); // reloaded: the server may keep an entry whose push is still queued
  });

  it('has nothing to clear in an empty inbox', async () => {
    mockedInbox.mockResolvedValue([]);
    await render(<NotificationsScreen />);
    await waitFor(() => screen.getByText('No notifications yet'));

    expect(lastOptions().headerRight()).toBeNull();
  });
});
