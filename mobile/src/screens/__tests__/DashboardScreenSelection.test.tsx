import { Alert, type AlertButton } from 'react-native';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import DashboardScreen from '../DashboardScreen';
import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { bulkDeleteTasks, bulkSetStatus } from '../../services/taskService';
import { TaskStatus, TaskType, type TaskWithTeam } from '../../types/task';

jest.mock('../../services/taskService');
jest.mock('../../services/listService');
jest.mock('../../services/userService', () => ({ getProfile: jest.fn().mockResolvedValue({ displayName: 'Mia', emailVerified: true }) }));
jest.mock('../../components/CalendarStatusChip', () => ({ CalendarStatusChip: () => null }));

const DAY = 86_400_000;
const inDays = (d: number) => new Date(Date.now() + d * DAY).toISOString();

const mockItem = (over: Partial<TaskWithTeam>): TaskWithTeam => ({
  id: 'x', teamId: 't', teamName: 'Team', assignedUserId: null, title: 'Item', description: null, status: TaskStatus.Backlog,
  deadlineUtc: inDays(3), impactScore: 5, blockedByTaskId: null, manualUrgencyOverride: null, manualUrgencyOverrideExpiresAtUtc: null,
  currentUrgencyScore: 0.4, createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: null, ...PLAIN_TASK_FIELDS, ...over,
} as TaskWithTeam);

const weekly = (id: string, days: number) =>
  mockItem({
    id, title: 'Weekly sync', taskType: TaskType.Event, status: TaskStatus.Confirmed, deadlineUtc: null, seriesId: 'weekly',
    startUtc: inDays(days), endUtc: new Date(Date.now() + days * DAY + 3_600_000).toISOString(), currentUrgencyScore: 0.3,
  });

let mockItems: TaskWithTeam[] = [];
const mockReload = jest.fn();
const mockNavigate = jest.fn();

jest.mock('../../hooks/useAllTasks', () => ({
  useAllTasks: () => ({
    state: 'ready', tasks: mockItems, lists: [], teams: [{ id: 't', name: 'Team', isPersonal: false }], errorMessage: null, refreshing: false,
    lastUpdated: null, refresh: jest.fn(), reload: mockReload,
  }),
}));
jest.mock('../../hooks/useTaskActions', () => ({ useTaskActions: () => ({}) }));
jest.mock('../../hooks/useUnreadCount', () => ({ useUnreadCount: () => ({ count: 0 }) }));
jest.mock('@react-navigation/native', () => {
  const React = require('react');
  return {
    useNavigation: () => ({ navigate: mockNavigate }),
    useScrollToTop: jest.fn(),
    useFocusEffect: (effect: () => void) => React.useEffect(effect, []),
  };
});

const mockedDelete = bulkDeleteTasks as jest.MockedFunction<typeof bulkDeleteTasks>;
const mockedStatus = bulkSetStatus as jest.MockedFunction<typeof bulkSetStatus>;

let alertSpy: jest.SpyInstance;
const pressAlertButton = async (text: string) => {
  const buttons = alertSpy.mock.calls[alertSpy.mock.calls.length - 1][2] as AlertButton[];
  await act(async () => {
    await buttons.find((b) => b.text === text)?.onPress?.();
  });
};

// Step 7.5.11 on the Dashboard: a repeating event is one card, a double tap starts the selection mode, "+" also makes a list.
describe('DashboardScreen - selection mode, series and lists', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    alertSpy = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);
    mockedDelete.mockResolvedValue({ succeeded: 0, skippedIds: [] });
    mockedStatus.mockResolvedValue({ succeeded: 0, skippedIds: [] });
    mockItems = [mockItem({ id: 'a', title: 'Write report', currentUrgencyScore: 0.9 }), mockItem({ id: 'b', title: 'Pay invoice' })];
  });

  afterEach(() => alertSpy.mockRestore());

  it('shows a repeating event once, with the number of the other occurrences', async () => {
    mockItems = [...mockItems, weekly('w1', 2), weekly('w2', 9), weekly('w3', 16)];

    await render(<DashboardScreen />);

    await waitFor(() => expect(screen.getByTestId('task-card-w1')).toBeTruthy());
    expect(screen.queryByTestId('task-card-w2')).toBeNull();
    expect(screen.getByText('🔁 +2 more')).toBeTruthy();
  });

  it('a double tap ticks a card - the "Do now" one too - and shows the bar; a tap on another card ticks it as well', async () => {
    await render(<DashboardScreen />);
    await waitFor(() => screen.getByTestId('task-card-a'));
    expect(screen.getByTestId('fab-new-task')).toBeTruthy();

    await fireEvent.press(screen.getByTestId('task-card-a'));
    await fireEvent.press(screen.getByTestId('task-card-a'));
    await fireEvent.press(screen.getByTestId('task-card-b'));

    expect(screen.getByTestId('selection-bar')).toBeTruthy();
    expect(screen.getByTestId('task-ticked-a')).toBeTruthy();
    expect(screen.getByTestId('task-ticked-b')).toBeTruthy();
    expect(screen.queryByTestId('fab-new-task')).toBeNull();
    expect(mockNavigate).not.toHaveBeenCalled();
  });

  it('the bar marks the ticked tasks completed in one request and leaves the mode', async () => {
    mockedStatus.mockResolvedValue({ succeeded: 2, skippedIds: [] });
    await render(<DashboardScreen />);
    await waitFor(() => screen.getByTestId('task-card-a'));
    await fireEvent.press(screen.getByTestId('task-card-a'));
    await fireEvent.press(screen.getByTestId('task-card-a'));
    await fireEvent.press(screen.getByTestId('task-card-b'));

    await fireEvent.press(screen.getByTestId('selection-status'));
    await fireEvent.press(screen.getByTestId(`status-option-${TaskStatus.Completed}`));

    await waitFor(() => expect(mockedStatus).toHaveBeenCalledWith('t', ['a', 'b'], TaskStatus.Completed));
    expect(screen.queryByTestId('selection-bar')).toBeNull();
  });

  it('deleting the ticked entry of a repeating series deletes all its occurrences', async () => {
    mockItems = [mockItem({ id: 'a', title: 'Write report' }), weekly('w1', 2), weekly('w2', 9)];
    mockedDelete.mockResolvedValue({ succeeded: 2, skippedIds: [] });
    await render(<DashboardScreen />);
    await waitFor(() => screen.getByTestId('task-card-w1'));
    await fireEvent.press(screen.getByTestId('task-card-w1'));
    await fireEvent.press(screen.getByTestId('task-card-w1'));

    await fireEvent.press(screen.getByTestId('selection-delete'));
    expect(alertSpy.mock.calls[0][0]).toBe('Delete 2 items?');
    await pressAlertButton('Delete');

    expect(mockedDelete).toHaveBeenCalledWith('t', ['w1', 'w2']);
  });

  it('the + menu offers "New list" and opens the list form', async () => {
    await render(<DashboardScreen />);
    await waitFor(() => screen.getByTestId('fab-new-task'));

    await fireEvent.press(screen.getByTestId('fab-new-task'));
    await fireEvent.press(screen.getByTestId('add-menu-list'));

    expect(screen.getByText('New list')).toBeTruthy();
    expect(screen.getByTestId('list-name-input')).toBeTruthy();
  });
});
