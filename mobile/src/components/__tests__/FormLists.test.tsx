import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { CreateTaskModal } from '../CreateTaskModal';
import { TaskEditForm } from '../TaskEditForm';
import { createList, getTeamLists } from '../../services/listService';
import { createTask, updateTask } from '../../services/taskService';
import { getTeamMembers } from '../../services/teamService';
import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { ItemListKind, type ItemListDto } from '../../types/itemList';
import { EventTemplate, TaskStatus, TaskType, type TaskDto } from '../../types/task';
import { TeamRole, type TeamDto } from '../../types/team';

jest.mock('../../services/taskService');
jest.mock('../../services/teamService');
jest.mock('../../services/listService');
jest.mock('../../services/authService', () => ({ getCurrentUserId: () => 'me' }));

const mockedCreateTask = createTask as jest.MockedFunction<typeof createTask>;
const mockedUpdateTask = updateTask as jest.MockedFunction<typeof updateTask>;
const mockedMembers = getTeamMembers as jest.MockedFunction<typeof getTeamMembers>;
const mockedLists = getTeamLists as jest.MockedFunction<typeof getTeamLists>;
const mockedCreateList = createList as jest.MockedFunction<typeof createList>;

const squad: TeamDto = { id: 's', name: 'Squad', description: null, memberCount: 3, isPersonal: false, muteNotifications: false, muteChat: false };
const list = (id: string, name: string, kind: ItemListKind): ItemListDto => ({ id, teamId: 's', name, kind, createdByUserId: 'me' });
const sprint = list('l-sprint', 'Sprint', ItemListKind.TaskList);
const standups = list('l-standups', 'Standups', ItemListKind.MeetingList);

const item = (id: string, title: string, over: Partial<TaskDto> = {}): TaskDto => ({
  id,
  teamId: 's',
  assignedUserId: 'me',
  title,
  description: null,
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
  ...over,
});

const open = (tasks: TaskDto[] = []) => render(<CreateTaskModal visible teams={[squad]} tasks={tasks} onClose={jest.fn()} onCreated={jest.fn()} />);

async function pickDeadlineTomorrow() {
  await fireEvent.press(screen.getByTestId('deadline-picker-button'));
  await fireEvent.press(screen.getByTestId('deadline-picker-quick-tomorrow'));
  await fireEvent.press(screen.getByTestId('deadline-picker-set'));
}

// The form of an item picks one of the team's own lists.
describe('CreateTaskModal - lists', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedMembers.mockResolvedValue([{ id: 'm1', userId: 'me', userDisplayName: 'Mia', role: TeamRole.Owner, joinedAtUtc: '2026-01-01T00:00:00Z' }]);
    mockedLists.mockResolvedValue([sprint, standups]);
    mockedCreateTask.mockResolvedValue({} as never);
  });

  it('offers None and the task lists of the team (not the meeting lists) and sends the chosen one', async () => {
    await open();
    await waitFor(() => expect(screen.getByTestId('list-chip-l-sprint')).toBeTruthy());
    expect(screen.queryByTestId('list-chip-l-standups')).toBeNull();

    await fireEvent.changeText(screen.getByTestId('create-task-title-input'), 'Report');
    await pickDeadlineTomorrow();
    await fireEvent.press(screen.getByTestId('list-chip-l-sprint'));
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));

    await waitFor(() => expect(mockedCreateTask).toHaveBeenCalled());
    expect(mockedCreateTask.mock.calls[0][1].itemListId).toBe('l-sprint');
  });

  it('sends no list when None is picked', async () => {
    await open();
    await waitFor(() => screen.getByTestId('list-chip-l-sprint'));
    await fireEvent.changeText(screen.getByTestId('create-task-title-input'), 'Report');
    await pickDeadlineTomorrow();

    await fireEvent.press(screen.getByTestId('create-task-submit-button'));

    await waitFor(() => expect(mockedCreateTask).toHaveBeenCalled());
    expect(mockedCreateTask.mock.calls[0][1].itemListId).toBeNull();
  });

  it('turning the item into an event shows the meeting lists and drops a task list that was picked', async () => {
    await open();
    await waitFor(() => screen.getByTestId('list-chip-l-sprint'));
    await fireEvent.press(screen.getByTestId('list-chip-l-sprint'));

    await fireEvent.press(screen.getByTestId('schedule-type-event'));

    expect(screen.getByTestId('list-chip-l-standups')).toBeTruthy();
    expect(screen.queryByTestId('list-chip-l-sprint')).toBeNull();
    // The pick is back to None: what is sent proves it.
    await fireEvent.changeText(screen.getByTestId('create-task-title-input'), 'Retro');
    await fireEvent.press(screen.getByTestId('event-start-picker-button'));
    await fireEvent.press(screen.getByTestId('event-start-picker-quick-week'));
    await fireEvent.press(screen.getByTestId('event-start-picker-set'));
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));
    await waitFor(() => expect(mockedCreateTask).toHaveBeenCalled());
    expect(mockedCreateTask.mock.calls[0][1].itemListId).toBeNull();
  });

  it('"+ New list" makes the list on the spot and picks it', async () => {
    mockedCreateList.mockResolvedValue(list('l-new', 'Backlog', ItemListKind.TaskList));
    await open();
    await waitFor(() => screen.getByTestId('list-chip-l-sprint'));

    await fireEvent.press(screen.getByTestId('list-chip-new'));
    await fireEvent.changeText(screen.getByTestId('list-picker-name'), 'Backlog');
    await fireEvent.press(screen.getByTestId('list-picker-add'));

    await waitFor(() => expect(mockedCreateList).toHaveBeenCalledWith('s', 'Backlog', ItemListKind.TaskList));
    expect(await screen.findByTestId('list-chip-l-new')).toBeTruthy();
    await fireEvent.changeText(screen.getByTestId('create-task-title-input'), 'Report');
    await pickDeadlineTomorrow();
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));
    await waitFor(() => expect(mockedCreateTask).toHaveBeenCalled());
    expect(mockedCreateTask.mock.calls[0][1].itemListId).toBe('l-new');
  });

  it('shows why a list could not be made and keeps the form', async () => {
    mockedCreateList.mockRejectedValue({ status: 409, message: 'A list named "Sprint" already exists.', isNetworkError: false });
    await open();
    await waitFor(() => screen.getByTestId('list-chip-l-sprint'));

    await fireEvent.press(screen.getByTestId('list-chip-new'));
    await fireEvent.changeText(screen.getByTestId('list-picker-name'), 'Sprint');
    await fireEvent.press(screen.getByTestId('list-picker-add'));

    expect(await screen.findByText('A list named "Sprint" already exists.')).toBeTruthy();
  });
});

describe('CreateTaskModal - "Blocked by" choices', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedMembers.mockResolvedValue([]);
    mockedLists.mockResolvedValue([]);
  });

  const birthday = (id: string, year: number) =>
    item(id, 'Anna birthday', {
      taskType: TaskType.Event, status: TaskStatus.Confirmed, eventTemplate: EventTemplate.Birthday, seriesId: 'birthday',
      startUtc: `${year}-10-20T00:00:00Z`, endUtc: `${year}-10-21T00:00:00Z`, isAllDay: true,
    });
  const weekly = (id: string, days: number) =>
    item(id, 'Weekly sync', {
      taskType: TaskType.Event, status: TaskStatus.Confirmed, seriesId: 'weekly',
      startUtc: new Date(Date.now() + days * 86_400_000).toISOString(), endUtc: new Date(Date.now() + days * 86_400_000 + 3_600_000).toISOString(),
    });

  it('leaves out birthdays and the like - an informational event holds nothing up', async () => {
    const birthdays = Array.from({ length: 5 }, (_, i) => birthday(`b${i}`, 2030 + i));

    await open([item('t1', 'Write report'), ...birthdays]);

    expect(screen.getByTestId('blocked-by-chip-t1')).toBeTruthy();
    expect(screen.queryByTestId('blocked-by-chip-b0')).toBeNull();
    expect(screen.queryByText('Anna birthday')).toBeNull();
  });

  it('offers a repeating meeting once, not once per date', async () => {
    await open([weekly('w1', 2), weekly('w2', 9), weekly('w3', 16), item('t1', 'Write report')]);

    expect(screen.getAllByText('Weekly sync')).toHaveLength(1);
    expect(screen.getByTestId('blocked-by-chip-w1')).toBeTruthy();
  });

  it('leaves out finished items', async () => {
    await open([item('t1', 'Write report'), item('t2', 'Done already', { status: TaskStatus.Completed })]);

    expect(screen.getByTestId('blocked-by-chip-t1')).toBeTruthy();
    expect(screen.queryByTestId('blocked-by-chip-t2')).toBeNull();
  });
});

describe('TaskEditForm - lists', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedLists.mockResolvedValue([sprint, standups]);
    mockedUpdateTask.mockResolvedValue({} as never);
  });

  const form = (task: TaskDto) => render(<TaskEditForm task={task} members={[]} canAssign={false} onSaved={jest.fn()} onCancel={jest.fn()} />);

  it('opens with the item\'s list picked and saves it back', async () => {
    await form(item('t1', 'Write report', { itemListId: 'l-sprint', teamId: 's' }));
    await waitFor(() => screen.getByTestId('list-chip-l-sprint'));

    await fireEvent.press(screen.getByTestId('edit-task-save'));

    await waitFor(() => expect(mockedUpdateTask).toHaveBeenCalled());
    expect(mockedUpdateTask.mock.calls[0][2].itemListId).toBe('l-sprint');
  });

  it('None takes the item out of its list', async () => {
    await form(item('t1', 'Write report', { itemListId: 'l-sprint', teamId: 's' }));
    await waitFor(() => screen.getByTestId('list-chip-l-sprint'));

    await fireEvent.press(screen.getByTestId('list-chip-none'));
    await fireEvent.press(screen.getByTestId('edit-task-save'));

    await waitFor(() => expect(mockedUpdateTask).toHaveBeenCalled());
    expect(mockedUpdateTask.mock.calls[0][2].itemListId).toBeNull();
  });

  it('an imported item can sit in a list too (the list is PlanFlow\'s own field)', async () => {
    await form(item('g1', 'Buy milk', { isExternal: true, isAllDay: true, googleTaskListId: 'shop', googleTaskListTitle: 'Shopping', teamId: 's' }));
    await waitFor(() => screen.getByTestId('list-chip-l-sprint'));

    await fireEvent.press(screen.getByTestId('list-chip-l-sprint'));
    await fireEvent.press(screen.getByTestId('edit-task-save'));

    await waitFor(() => expect(mockedUpdateTask).toHaveBeenCalled());
    expect(mockedUpdateTask.mock.calls[0][2].itemListId).toBe('l-sprint');
  });
});
