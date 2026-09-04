import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import OneNextActionScreen from '../OneNextActionScreen';
import { getTeamTasks, updateTask } from '../../services/taskService';
import { TaskStatus, type TaskDto } from '../../types/task';

jest.mock('../../services/taskService');

const mockNavigate = jest.fn();

jest.mock('@react-navigation/native', () => ({
  useRoute: () => ({ params: { teamId: 'team-1' } }),
  useNavigation: () => ({ navigate: mockNavigate }),
  // Real useFocusEffect re-runs on every screen focus via useEffect; a plain synchronous call
  // during render would trigger setState-in-render and an infinite loop under RTL.
  useFocusEffect: (callback: () => void) => require('react').useEffect(callback, []),
}));

const mockedGetTeamTasks = getTeamTasks as jest.MockedFunction<typeof getTeamTasks>;
const mockedUpdateTask = updateTask as jest.MockedFunction<typeof updateTask>;

function buildTask(overrides: Partial<TaskDto> = {}): TaskDto {
  return {
    id: 'task-1',
    teamId: 'team-1',
    assignedUserId: null,
    title: 'Write the thesis chapter',
    description: 'Chapter 3 draft',
    status: TaskStatus.Todo,
    deadlineUtc: null,
    impactScore: 5,
    blockedByTaskId: null,
    manualUrgencyOverride: null,
    manualUrgencyOverrideExpiresAtUtc: null,
    currentUrgencyScore: 0.85,
    createdAtUtc: '2026-01-01T00:00:00.000Z',
    updatedAtUtc: null,
    ...overrides,
  };
}

describe('OneNextActionScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('shows the empty state when there are no eligible tasks', async () => {
    mockedGetTeamTasks.mockResolvedValue([buildTask({ status: TaskStatus.Done })]);

    await render(<OneNextActionScreen />);

    await waitFor(() => expect(screen.getByTestId('one-next-action-empty')).toBeTruthy());
  });

  it('renders the top-ranked task', async () => {
    mockedGetTeamTasks.mockResolvedValue([
      buildTask({ id: 'task-low', currentUrgencyScore: 0.2, title: 'Low priority' }),
      buildTask({ id: 'task-1', currentUrgencyScore: 0.85 }),
    ]);

    await render(<OneNextActionScreen />);

    await waitFor(() => expect(screen.getByText('Write the thesis chapter')).toBeTruthy());
    expect(screen.queryByText('Low priority')).toBeNull();
  });

  it('marks the active task complete and updates the view to the next candidate', async () => {
    const secondTask = buildTask({ id: 'task-2', title: 'Second task', currentUrgencyScore: 0.5 });
    mockedGetTeamTasks.mockResolvedValue([buildTask(), secondTask]);
    mockedUpdateTask.mockResolvedValue({ ...buildTask(), status: TaskStatus.Done });

    await render(<OneNextActionScreen />);
    await waitFor(() => expect(screen.getByText('Write the thesis chapter')).toBeTruthy());

    fireEvent.press(screen.getByTestId('focus-complete-button'));

    await waitFor(() => expect(mockedUpdateTask).toHaveBeenCalledWith('task-1', expect.objectContaining({ status: TaskStatus.Done })));
    await waitFor(() => expect(screen.getByText('Second task')).toBeTruthy());
  });

  it('defers the active task with a 24h manual urgency override', async () => {
    mockedGetTeamTasks.mockResolvedValue([buildTask()]);
    mockedUpdateTask.mockResolvedValue({ ...buildTask(), manualUrgencyOverride: 0 });

    await render(<OneNextActionScreen />);
    await waitFor(() => expect(screen.getByText('Write the thesis chapter')).toBeTruthy());

    fireEvent.press(screen.getByTestId('focus-defer-button'));

    await waitFor(() =>
      expect(mockedUpdateTask).toHaveBeenCalledWith(
        'task-1',
        expect.objectContaining({ manualUrgencyOverride: 0, manualUrgencyOverrideExpiresAtUtc: expect.any(String) }),
      ),
    );
  });

  it('navigates to TaskDetail when breaking down the active task', async () => {
    mockedGetTeamTasks.mockResolvedValue([buildTask()]);

    await render(<OneNextActionScreen />);
    await waitFor(() => expect(screen.getByText('Write the thesis chapter')).toBeTruthy());

    fireEvent.press(screen.getByTestId('focus-breakdown-button'));

    expect(mockNavigate).toHaveBeenCalledWith('TaskDetail', { taskId: 'task-1' });
  });
});
