import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { CreateTaskModal } from '../CreateTaskModal';
import { createTask } from '../../services/taskService';
import { getTeamMembers } from '../../services/teamService';
import { ReminderAnchor, TaskType } from '../../types/task';
import { TeamRole, type TeamDto, type TeamMemberDto } from '../../types/team';

jest.mock('../../services/taskService');
jest.mock('../../services/teamService');
jest.mock('../../services/authService', () => ({ getCurrentUserId: () => 'me' }));

const mockedCreateTask = createTask as jest.MockedFunction<typeof createTask>;
const mockedGetTeamMembers = getTeamMembers as jest.MockedFunction<typeof getTeamMembers>;

const personal: TeamDto = { id: 'p', name: 'Me', description: null, memberCount: 1, isPersonal: true, muteNotifications: false, muteChat: false };
const squad: TeamDto = { id: 's', name: 'Squad', description: null, memberCount: 3, isPersonal: false, muteNotifications: false, muteChat: false };
const member = (userId: string, name: string, role = TeamRole.Member): TeamMemberDto => ({ id: `m-${userId}`, userId, userDisplayName: name, role, joinedAtUtc: '2026-01-01T00:00:00Z' });

const open = (teams: TeamDto[]) => render(<CreateTaskModal visible teams={teams} tasks={[]} onClose={jest.fn()} onCreated={jest.fn()} />);

async function pickDeadlineTomorrow() {
  await fireEvent.press(screen.getByTestId('deadline-picker-button'));
  await fireEvent.press(screen.getByTestId('deadline-picker-quick-tomorrow'));
  await fireEvent.press(screen.getByTestId('deadline-picker-set'));
}

async function pickEventStartInAWeek() {
  await fireEvent.press(screen.getByTestId('event-start-picker-button'));
  await fireEvent.press(screen.getByTestId('event-start-picker-quick-week'));
  await fireEvent.press(screen.getByTestId('event-start-picker-set'));
}

// Alerts and the people invited are part of the create form.
describe('CreateTaskModal - alerts and guests', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedGetTeamMembers.mockResolvedValue([member('me', 'Mia', TeamRole.Owner), member('ann', 'Ann'), member('ben', 'Ben')]);
    mockedCreateTask.mockResolvedValue({} as never);
  });

  it('sends the alerts of a task, counted from its deadline', async () => {
    await open([squad]);
    await fireEvent.changeText(screen.getByTestId('create-task-title-input'), 'Report');
    await pickDeadlineTomorrow();

    await fireEvent.changeText(screen.getByTestId('reminder-amount-input'), '2');
    await fireEvent.press(screen.getByTestId('reminder-unit-hours'));
    await fireEvent.press(screen.getByTestId('reminder-add'));
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));

    await waitFor(() => expect(mockedCreateTask).toHaveBeenCalled());
    expect(mockedCreateTask.mock.calls[0][1].reminders).toEqual([
      { offsetMinutes: 120, daysBefore: null, timeOfDayMinutes: null, anchor: ReminderAnchor.Deadline },
    ]);
  });

  it('refuses a "before the start" alert when the task has no start, and does not call the server', async () => {
    await open([squad]);
    await fireEvent.changeText(screen.getByTestId('create-task-title-input'), 'Report');
    await pickDeadlineTomorrow();

    await fireEvent.press(screen.getByTestId('reminder-anchor-start'));
    await fireEvent.press(screen.getByTestId('reminder-add'));
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));

    expect(screen.getByText('Set a start first: an alert counts back from it.')).toBeTruthy();
    expect(mockedCreateTask).not.toHaveBeenCalled();
  });

  it('an event is reminded from its start; a kind without alerts shows no editor', async () => {
    await open([squad]);
    await fireEvent.press(screen.getByTestId('schedule-type-event'));
    await fireEvent.changeText(screen.getByTestId('create-task-title-input'), 'Retro');
    await pickEventStartInAWeek();
    await fireEvent.press(screen.getByTestId('reminder-add'));
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));

    await waitFor(() => expect(mockedCreateTask).toHaveBeenCalled());
    expect(mockedCreateTask.mock.calls[0][1].reminders).toEqual([
      { offsetMinutes: 10, daysBefore: null, timeOfDayMinutes: null, anchor: ReminderAnchor.Start },
    ]);

    await fireEvent.press(screen.getByTestId('schedule-template-out-of-office'));
    expect(screen.queryByTestId('reminders-editor')).toBeNull();
  });

  it('switching to a kind without alerts sends none (and they return if the person switches back)', async () => {
    await open([squad]);
    await fireEvent.press(screen.getByTestId('schedule-type-event'));
    await fireEvent.press(screen.getByTestId('reminder-add'));

    await fireEvent.press(screen.getByTestId('schedule-template-working-location'));
    await pickEventStartInAWeek();
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));
    await waitFor(() => expect(mockedCreateTask).toHaveBeenCalled());
    expect(mockedCreateTask.mock.calls[0][1].reminders).toEqual([]);
  });

  it('invites co-contractors to a task in a shared team, never the assignee', async () => {
    await open([squad]);
    await fireEvent.changeText(screen.getByTestId('create-task-title-input'), 'Report');
    await pickDeadlineTomorrow();
    await waitFor(() => expect(screen.getByTestId('participant-picker')).toBeTruthy());
    expect(screen.getByText('Co-contractors (optional):')).toBeTruthy();

    // Mia (the owner) assigns it to Ann; Ann cannot also be a co-contractor, but Ben can.
    await fireEvent.press(screen.getByTestId('participant-chip-ann'));
    await fireEvent.press(screen.getByTestId('participant-chip-ben'));
    await fireEvent.press(screen.getByTestId('assignee-chip-ann'));
    expect(screen.queryByTestId('participant-chip-ann')).toBeNull();
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));

    await waitFor(() => expect(mockedCreateTask).toHaveBeenCalled());
    expect(mockedCreateTask.mock.calls[0][1]).toMatchObject({ assignedUserId: 'ann', participantUserIds: ['ben'] });
  });

  it('calls them guests for an event, and offers none for Out of office or in a personal workspace', async () => {
    await open([squad]);
    await fireEvent.press(screen.getByTestId('schedule-type-event'));
    await waitFor(() => expect(screen.getByText('Guests (optional):')).toBeTruthy());

    await fireEvent.press(screen.getByTestId('schedule-template-out-of-office'));
    expect(screen.queryByTestId('participant-picker')).toBeNull();
  });

  it('a personal workspace has nobody to invite', async () => {
    mockedGetTeamMembers.mockResolvedValue([member('me', 'Mia', TeamRole.Owner)]);
    await open([personal]);
    await fireEvent.changeText(screen.getByTestId('create-task-title-input'), 'Solo');
    await pickDeadlineTomorrow();

    expect(screen.queryByTestId('participant-picker')).toBeNull();
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));
    await waitFor(() => expect(mockedCreateTask).toHaveBeenCalled());
    expect(mockedCreateTask.mock.calls[0][1].participantUserIds).toEqual([]);
    expect(mockedCreateTask.mock.calls[0][1]).toMatchObject({ taskType: TaskType.Task });
  });
});
