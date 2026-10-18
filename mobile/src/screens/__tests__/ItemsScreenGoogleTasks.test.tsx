import { fireEvent, render, screen } from '@testing-library/react-native';
import ItemsScreen from '../ItemsScreen';
import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { TaskStatus, TaskType, type TaskWithTeam } from '../../types/task';

const mockItem = (over: Partial<TaskWithTeam>): TaskWithTeam => ({
  id: 'x', teamId: 't', teamName: 'Team', assignedUserId: null, title: 'Item', description: null, status: TaskStatus.Backlog,
  deadlineUtc: null, impactScore: null, blockedByTaskId: null, manualUrgencyOverride: null, manualUrgencyOverrideExpiresAtUtc: null,
  currentUrgencyScore: 0, createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: null, ...PLAIN_TASK_FIELDS, ...over,
} as TaskWithTeam);

const google = (id: string, title: string, listId: string, listTitle: string) =>
  mockItem({
    id, title, isExternal: true, isAllDay: true, deadlineUtc: '2030-10-20T00:00:00.000Z',
    googleTaskListId: listId, googleTaskListTitle: listTitle,
  });

const mockItems = [
  mockItem({ id: 'own', title: 'Write report' }),
  google('g1', 'Buy milk', 'shop', 'Shopping'),
  google('g2', 'Buy bread', 'shop', 'Shopping'),
  google('g3', 'Book flights', 'trip', 'Holiday'),
  mockItem({ id: 'ev', title: 'Dentist', taskType: TaskType.Event, startUtc: new Date(Date.now() + 5 * 3_600_000).toISOString() }),
];

jest.mock('../../hooks/useAllTasks', () => ({
  useAllTasks: () => ({
    state: 'ready', tasks: mockItems, teams: [], errorMessage: null, refreshing: false, refresh: jest.fn(), reload: jest.fn(),
  }),
}));
jest.mock('../../hooks/useTaskActions', () => ({ useTaskActions: () => ({}) }));
jest.mock('@react-navigation/native', () => ({ useNavigation: () => ({ navigate: jest.fn() }), useScrollToTop: jest.fn() }));

// Step 7.5.3: the tasks of each Google Tasks list sit in their own collapsed group under your own tasks.
describe('ItemsScreen - Google Tasks lists', () => {
  it('shows one collapsed group per list with a count, and your own tasks as usual', async () => {
    await render(<ItemsScreen />);

    expect(screen.getByText('Write report')).toBeTruthy();
    expect(screen.getByText('Shopping')).toBeTruthy();
    expect(screen.getByText('Holiday')).toBeTruthy();
    expect(screen.getByText('2 tasks · Google Tasks')).toBeTruthy();
    expect(screen.getByText('1 task · Google Tasks')).toBeTruthy();
    expect(screen.getByTestId('google-list-shop-header').props.accessibilityState).toEqual({ expanded: false });
    expect(screen.queryByText('Buy milk')).toBeNull(); // collapsed: the tasks are not shown
    expect(screen.queryByText('Book flights')).toBeNull();
  });

  it('opens a group on tap and shows its tasks (and only its tasks)', async () => {
    await render(<ItemsScreen />);

    await fireEvent.press(screen.getByTestId('google-list-shop-header'));

    expect(screen.getByText('Buy milk')).toBeTruthy();
    expect(screen.getByText('Buy bread')).toBeTruthy();
    expect(screen.queryByText('Book flights')).toBeNull();
  });

  it('counts the grouped tasks in the Tasks heading and the tab, and keeps events out of the groups', async () => {
    await render(<ItemsScreen />);

    expect(screen.getByText('Tasks · 4')).toBeTruthy(); // own + the three Google tasks
    expect(screen.getByText('Tasks (4)')).toBeTruthy();
    expect(screen.getByText('Dentist')).toBeTruthy();

    await fireEvent.press(screen.getByTestId('items-tab-events'));
    expect(screen.queryByText('Shopping')).toBeNull();
  });
});
