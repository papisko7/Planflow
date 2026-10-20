import { fireEvent, render, screen } from '@testing-library/react-native';
import { CreateTaskModal } from '../CreateTaskModal';
import { TaskEditForm } from '../TaskEditForm';
import { getTeamMembers } from '../../services/teamService';
import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { TaskStatus, TaskType, type TaskDto } from '../../types/task';
import type { TeamDto } from '../../types/team';

jest.mock('../../services/taskService');
jest.mock('../../services/teamService');
jest.mock('../../services/authService', () => ({ getCurrentUserId: () => 'me' }));

const mockedGetTeamMembers = getTeamMembers as jest.MockedFunction<typeof getTeamMembers>;
const personal: TeamDto = { id: 'p', name: 'Me', description: null, memberCount: 1, isPersonal: true, muteNotifications: false, muteChat: false };

const task: TaskDto = {
  id: 't1',
  teamId: 'p',
  assignedUserId: 'me',
  title: 'Write chapter 3',
  description: 'Sources',
  status: TaskStatus.Backlog,
  deadlineUtc: '2030-10-25T17:00:00.000Z',
  impactScore: 5,
  blockedByTaskId: null,
  manualUrgencyOverride: null,
  manualUrgencyOverrideExpiresAtUtc: null,
  currentUrgencyScore: 0.3,
  createdAtUtc: '2026-10-01T00:00:00Z',
  updatedAtUtc: null,
  ...PLAIN_TASK_FIELDS,
  taskType: TaskType.Task,
};

// The fields of an item are named by a caption above them (with a line saying what they are for), never by text inside the field,
// so an item that is being edited shows what each value means.
describe('field captions', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedGetTeamMembers.mockResolvedValue([]);
  });

  it('a new task: every field has a caption and none has a placeholder', async () => {
    await render(<CreateTaskModal visible teams={[personal]} tasks={[]} onClose={jest.fn()} onCreated={jest.fn()} />);

    ['Title', 'Description (optional)', 'Start (optional)', 'Deadline (optional)', 'Estimated effort (optional)', 'Impact (0-10)'].forEach((caption) =>
      expect(screen.getByText(caption)).toBeTruthy(),
    );
    ['create-task-title-input', 'create-task-description-input', 'effort-input', 'create-task-impact-input'].forEach((id) =>
      expect(screen.getByTestId(id).props.placeholder).toBeUndefined(),
    );
    expect(screen.getByText(/Empty = not rated/)).toBeTruthy(); // what the impact field does when left empty
  });

  it('a new event: the place and the effort are captioned too, and the effort says what it means for an event', async () => {
    await render(<CreateTaskModal visible teams={[personal]} tasks={[]} onClose={jest.fn()} onCreated={jest.fn()} />);
    await fireEvent.press(screen.getByTestId('schedule-type-event'));

    ['Start', 'End', 'Location (optional)', 'Estimated effort (optional)'].forEach((caption) => expect(screen.getByText(caption)).toBeTruthy());
    expect(screen.getByTestId('schedule-location-input').props.placeholder).toBeUndefined();
    expect(screen.getByText(/Work to do before it starts/)).toBeTruthy();
  });

  it('editing a task shows the same captions above the filled-in values', async () => {
    await render(<TaskEditForm task={task} members={[]} canAssign={false} onSaved={jest.fn()} onCancel={jest.fn()} />);

    ['Title', 'Description (optional)', 'Deadline (optional)', 'Estimated effort (optional)', 'Impact (0-10)'].forEach((caption) =>
      expect(screen.getByText(caption)).toBeTruthy(),
    );
    expect(screen.getByTestId('edit-task-title-input').props.placeholder).toBeUndefined();
    expect(screen.getByTestId('edit-task-impact-input').props.placeholder).toBeUndefined();
    expect(screen.getByTestId('edit-task-impact-input').props.value).toBe('5');
  });

  it('the day picker names its field above the button and shows "Not set" while it is empty', async () => {
    await render(<CreateTaskModal visible teams={[personal]} tasks={[]} onClose={jest.fn()} onCreated={jest.fn()} />);

    expect(screen.getByTestId('deadline-picker-button')).toHaveTextContent('Not set');
  });
});
