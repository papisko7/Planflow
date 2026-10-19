import { Alert, type AlertButton } from 'react-native';
import { act, fireEvent, render, screen } from '@testing-library/react-native';
import CalendarScreen from '../CalendarScreen';
import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { bulkDeleteTasks } from '../../services/taskService';
import { TaskStatus, TaskType, type TaskWithTeam } from '../../types/task';

jest.mock('../../services/taskService');
jest.mock('../../services/listService');
jest.mock('../../components/GoogleCalendarBanner', () => ({ GoogleCalendarBanner: () => null }));

const DAY = 86_400_000;

const mockItem = (over: Partial<TaskWithTeam>): TaskWithTeam => ({
  id: 'x', teamId: 't', teamName: 'Team', assignedUserId: null, title: 'Item', description: null, status: TaskStatus.Confirmed,
  deadlineUtc: null, impactScore: 5, blockedByTaskId: null, manualUrgencyOverride: null, manualUrgencyOverrideExpiresAtUtc: null,
  currentUrgencyScore: 0.4, createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: null, ...PLAIN_TASK_FIELDS, ...over,
} as TaskWithTeam);

// Noon, a few days ahead: far enough from midnight that the local day is the same in every time zone.
const noonIn = (days: number) => {
  const d = new Date(Date.now() + days * DAY);
  d.setHours(12, 0, 0, 0);
  return d;
};
const weekly = (id: string, days: number) =>
  mockItem({
    id, title: 'Weekly sync', taskType: TaskType.Event, seriesId: 'weekly',
    startUtc: noonIn(days).toISOString(), endUtc: new Date(noonIn(days).getTime() + 3_600_000).toISOString(),
  });

let mockItems: TaskWithTeam[] = [];
const mockReload = jest.fn();

jest.mock('../../hooks/useAllTasks', () => ({
  useAllTasks: () => ({
    state: 'ready', tasks: mockItems, lists: [], teams: [{ id: 't', name: 'Team', isPersonal: false }], errorMessage: null, refreshing: false,
    refresh: jest.fn(), reload: mockReload,
  }),
}));
jest.mock('../../hooks/useTaskActions', () => ({ useTaskActions: () => ({}) }));
jest.mock('@react-navigation/native', () => ({ useNavigation: () => ({ navigate: jest.fn() }), useScrollToTop: jest.fn() }));

const mockedDelete = bulkDeleteTasks as jest.MockedFunction<typeof bulkDeleteTasks>;

let alertSpy: jest.SpyInstance;
const pressAlertButton = async (text: string) => {
  const buttons = alertSpy.mock.calls[alertSpy.mock.calls.length - 1][2] as AlertButton[];
  await act(async () => {
    await buttons.find((b) => b.text === text)?.onPress?.();
  });
};

// On the Agenda: every occurrence keeps its own day (with the year in the heading), and the cards can be ticked.
describe('CalendarScreen - Agenda', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    alertSpy = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);
    mockedDelete.mockResolvedValue({ succeeded: 1, skippedIds: [] });
    mockItems = [weekly('w1', 2), weekly('w2', 9), weekly('w3', 16)];
  });

  afterEach(() => alertSpy.mockRestore());

  it('keeps every occurrence of a repeating event on its own day, each under a heading that has the year', async () => {
    await render(<CalendarScreen />);

    expect(screen.getByTestId('task-card-w1')).toBeTruthy();
    expect(screen.getByTestId('task-card-w2')).toBeTruthy();
    expect(screen.getByTestId('task-card-w3')).toBeTruthy();
    expect(screen.queryByTestId('series-more-w1')).toBeNull(); // the Agenda is not collapsed
    const year = String(noonIn(2).getFullYear());
    expect(screen.getAllByText(new RegExp(year)).length).toBeGreaterThanOrEqual(3);
  });

  it('a double tap ticks one occurrence, and deleting from the bar removes just that occurrence', async () => {
    await render(<CalendarScreen />);

    await fireEvent.press(screen.getByTestId('task-card-w2'));
    await fireEvent.press(screen.getByTestId('task-card-w2'));
    expect(screen.getByTestId('selection-bar')).toBeTruthy();
    expect(screen.getByTestId('task-ticked-w2')).toBeTruthy();
    expect(screen.getByTestId('task-unticked-w1')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('selection-delete'));
    expect(alertSpy.mock.calls[0][0]).toBe('Delete this item?');
    await pressAlertButton('Delete');

    expect(mockedDelete).toHaveBeenCalledWith('t', ['w2']);
  });

  it('"Select all" ticks everything the Agenda lists', async () => {
    await render(<CalendarScreen />);
    await fireEvent.press(screen.getByTestId('task-card-w1'));
    await fireEvent.press(screen.getByTestId('task-card-w1'));

    await fireEvent.press(screen.getByTestId('selection-select-all'));

    expect(screen.getByTestId('task-ticked-w2')).toBeTruthy();
    expect(screen.getByTestId('task-ticked-w3')).toBeTruthy();
  });
});
