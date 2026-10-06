import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { CreateTaskModal } from '../CreateTaskModal';
import { createTask } from '../../services/taskService';
import { getTeamMembers } from '../../services/teamService';
import type { TeamDto } from '../../types/team';

jest.mock('../../services/taskService');
jest.mock('../../services/teamService');
jest.mock('../../services/authService', () => ({ getCurrentUserId: () => 'me' }));

const mockedCreateTask = createTask as jest.MockedFunction<typeof createTask>;
const mockedGetTeamMembers = getTeamMembers as jest.MockedFunction<typeof getTeamMembers>;

const personal: TeamDto = { id: 'p', name: 'Me', description: null, memberCount: 1, isPersonal: true, muteNotifications: false, muteChat: false };
const shared: TeamDto = { id: 's', name: 'Squad', description: null, memberCount: 3, isPersonal: false, muteNotifications: false, muteChat: false };

describe('CreateTaskModal', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedGetTeamMembers.mockResolvedValue([]);
  });

  it('creates the task in the default team, assigned to the current user, then closes', async () => {
    const onCreated = jest.fn();
    const onClose = jest.fn();
    mockedCreateTask.mockResolvedValue({} as never);

    await render(
      <CreateTaskModal visible teams={[personal, shared]} tasks={[]} defaultTeamId="s" onClose={onClose} onCreated={onCreated} />,
    );
    await fireEvent.changeText(screen.getByTestId('create-task-title-input'), '  Buy milk ');
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));

    await waitFor(() =>
      expect(mockedCreateTask).toHaveBeenCalledWith('s', expect.objectContaining({ title: 'Buy milk', assignedUserId: 'me' })),
    );
    expect(onCreated).toHaveBeenCalled();
    expect(onClose).toHaveBeenCalled();
  });

  it('refuses an empty title without calling the API', async () => {
    await render(<CreateTaskModal visible teams={[personal]} tasks={[]} onClose={jest.fn()} onCreated={jest.fn()} />);

    await fireEvent.press(screen.getByTestId('create-task-submit-button'));

    expect(await screen.findByText('Title is required.')).toBeTruthy();
    expect(mockedCreateTask).not.toHaveBeenCalled();
  });

  it('sends an empty impact as null (not rated), not as 0', async () => {
    mockedCreateTask.mockResolvedValue({} as never);

    await render(<CreateTaskModal visible teams={[personal]} tasks={[]} onClose={jest.fn()} onCreated={jest.fn()} />);
    await fireEvent.changeText(screen.getByTestId('create-task-title-input'), 'Plan trip');
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));

    await waitFor(() => expect(mockedCreateTask).toHaveBeenCalledWith('p', expect.objectContaining({ impactScore: null })));
  });

  it('sends a typed impact as a number', async () => {
    mockedCreateTask.mockResolvedValue({} as never);

    await render(<CreateTaskModal visible teams={[personal]} tasks={[]} onClose={jest.fn()} onCreated={jest.fn()} />);
    await fireEvent.changeText(screen.getByTestId('create-task-title-input'), 'Plan trip');
    await fireEvent.changeText(screen.getByTestId('create-task-impact-input'), '7');
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));

    await waitFor(() => expect(mockedCreateTask).toHaveBeenCalledWith('p', expect.objectContaining({ impactScore: 7 })));
  });
});
