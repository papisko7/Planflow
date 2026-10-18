import { fireEvent, render, screen } from '@testing-library/react-native';
import ItemsScreen from '../ItemsScreen';
import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { TaskStatus, TaskType, type TaskWithTeam } from '../../types/task';

const HOUR = 3_600_000;
const inHours = (h: number) => new Date(Date.now() + h * HOUR).toISOString();

const mockItem = (over: Partial<TaskWithTeam>): TaskWithTeam => ({
  id: 'x', teamId: 't', teamName: 'Team', assignedUserId: null, title: 'Item', description: null, status: TaskStatus.Backlog,
  deadlineUtc: null, impactScore: null, blockedByTaskId: null, manualUrgencyOverride: null, manualUrgencyOverrideExpiresAtUtc: null,
  currentUrgencyScore: 0, createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: null, ...PLAIN_TASK_FIELDS, ...over,
} as TaskWithTeam);

const mockItems = [
  mockItem({ id: 'a', title: 'Write report' }),
  mockItem({ id: 'b', title: 'Dentist', taskType: TaskType.Event, startUtc: inHours(5), endUtc: inHours(6) }),
];

jest.mock('../../hooks/useAllTasks', () => ({
  useAllTasks: () => ({
    state: 'ready', tasks: mockItems, teams: [], errorMessage: null, refreshing: false, refresh: jest.fn(), reload: jest.fn(),
  }),
}));
jest.mock('../../hooks/useTaskActions', () => ({ useTaskActions: () => ({}) }));
jest.mock('@react-navigation/native', () => ({ useNavigation: () => ({ navigate: jest.fn() }), useScrollToTop: jest.fn() }));

describe('ItemsScreen', () => {
  it('opens on All: tasks and events together, split into a Tasks and an Events section', async () => {
    await render(<ItemsScreen />);

    expect(screen.getByText('All (2)')).toBeTruthy();
    expect(screen.getByTestId('items-section-tasks')).toBeTruthy();
    expect(screen.getByTestId('items-section-events')).toBeTruthy();
    expect(screen.getByText('Write report')).toBeTruthy();
    expect(screen.getByText('Dentist')).toBeTruthy();
  });

  it('the Tasks and Events tabs filter the list and drop the section headings', async () => {
    await render(<ItemsScreen />);

    await fireEvent.press(screen.getByTestId('items-tab-events'));
    expect(screen.queryByText('Write report')).toBeNull();
    expect(screen.getByText('Dentist')).toBeTruthy();
    expect(screen.queryByTestId('items-section-events')).toBeNull();

    await fireEvent.press(screen.getByTestId('items-tab-tasks'));
    expect(screen.getByText('Write report')).toBeTruthy();
    expect(screen.queryByText('Dentist')).toBeNull();

    await fireEvent.press(screen.getByTestId('items-tab-all'));
    expect(screen.getByText('Dentist')).toBeTruthy();
    expect(screen.getByText('Write report')).toBeTruthy();
  });
});
