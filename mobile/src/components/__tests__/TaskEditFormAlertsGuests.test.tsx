import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { TaskEditForm } from '../TaskEditForm';
import { updateTask } from '../../services/taskService';
import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { ReminderAnchor, TaskStatus, TaskType, type ReminderDto, type TaskDto } from '../../types/task';
import { TeamRole, type TeamMemberDto } from '../../types/team';

jest.mock('../../services/taskService');
jest.mock('../../services/authService', () => ({ getCurrentUserId: () => 'me' }));

const mockedUpdateTask = updateTask as jest.MockedFunction<typeof updateTask>;

const member = (userId: string, name: string): TeamMemberDto => ({ id: `m-${userId}`, userId, userDisplayName: name, role: TeamRole.Member, joinedAtUtc: '2026-01-01T00:00:00Z' });
const members = [member('me', 'Mia'), member('ann', 'Ann'), member('ben', 'Ben')];

const task: TaskDto = {
  id: 't1',
  teamId: 'team1',
  assignedUserId: 'me',
  title: 'Write report',
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
  participantUserIds: ['ben'],
};

const form = (item: TaskDto, reminders?: ReminderDto[], team = members) =>
  render(<TaskEditForm task={item} members={team} canAssign={false} reminders={reminders} onSaved={jest.fn()} onCancel={jest.fn()} />);

// The edit form opens with the item's alerts and people, and saves them back.
describe('TaskEditForm - alerts and co-contractors', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedUpdateTask.mockResolvedValue({} as never);
  });

  it('opens with the stored alerts and co-contractors, and saves them untouched', async () => {
    await form(task, [{ offsetMinutes: 60, daysBefore: null, timeOfDayMinutes: null, anchor: ReminderAnchor.Deadline }]);

    expect(screen.getByText('1 hour before the deadline')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('edit-task-save'));

    await waitFor(() => expect(mockedUpdateTask).toHaveBeenCalled());
    expect(mockedUpdateTask.mock.calls[0][2]).toMatchObject({
      reminders: [{ offsetMinutes: 60, daysBefore: null, timeOfDayMinutes: null, anchor: ReminderAnchor.Deadline }],
      participantUserIds: ['ben'],
    });
  });

  it('adds and removes alerts and people, and sends the new lists', async () => {
    await form(task, [{ offsetMinutes: 60, daysBefore: null, timeOfDayMinutes: null, anchor: ReminderAnchor.Deadline }]);

    await fireEvent.press(screen.getByTestId('reminder-remove-0'));
    await fireEvent.changeText(screen.getByTestId('reminder-amount-input'), '1');
    await fireEvent.press(screen.getByTestId('reminder-unit-days'));
    await fireEvent.press(screen.getByTestId('reminder-add'));
    await fireEvent.press(screen.getByTestId('participant-chip-ben')); // off
    await fireEvent.press(screen.getByTestId('participant-chip-ann')); // on
    await fireEvent.press(screen.getByTestId('edit-task-save'));

    await waitFor(() => expect(mockedUpdateTask).toHaveBeenCalled());
    expect(mockedUpdateTask.mock.calls[0][2]).toMatchObject({
      reminders: [{ offsetMinutes: 1440, daysBefore: null, timeOfDayMinutes: null, anchor: ReminderAnchor.Deadline }],
      participantUserIds: ['ann'],
    });
  });

  it('removing the last alert sends an empty list (which clears them), not nothing', async () => {
    await form(task, [{ offsetMinutes: 60, daysBefore: null, timeOfDayMinutes: null }]);

    await fireEvent.press(screen.getByTestId('reminder-remove-0'));
    await fireEvent.press(screen.getByTestId('edit-task-save'));

    await waitFor(() => expect(mockedUpdateTask).toHaveBeenCalled());
    expect(mockedUpdateTask.mock.calls[0][2].reminders).toEqual([]);
  });

  it('a personal workspace (one member) has no people to invite and sends no list of them', async () => {
    await form({ ...task, participantUserIds: [] }, [], [member('me', 'Mia')]);

    expect(screen.queryByTestId('participant-picker')).toBeNull();
    await fireEvent.press(screen.getByTestId('edit-task-save'));

    await waitFor(() => expect(mockedUpdateTask).toHaveBeenCalled());
    expect(mockedUpdateTask.mock.calls[0][2].participantUserIds).toBeUndefined();
  });

  it('an alert from the start of a task that has none is refused with a message', async () => {
    await form(task, []);

    await fireEvent.press(screen.getByTestId('reminder-anchor-start'));
    await fireEvent.press(screen.getByTestId('reminder-add'));
    await fireEvent.press(screen.getByTestId('edit-task-save'));

    expect(screen.getByText('Set a start first: an alert counts back from it.')).toBeTruthy();
    expect(mockedUpdateTask).not.toHaveBeenCalled();
  });
});

describe('TaskEditForm - imported items', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedUpdateTask.mockResolvedValue({} as never);
  });

  it('an event imported from Google keeps Google\'s alerts: no editor, none sent', async () => {
    const imported: TaskDto = {
      ...task, taskType: TaskType.Event, status: TaskStatus.Confirmed, isExternal: true, deadlineUtc: null,
      startUtc: '2030-10-19T09:00:00.000Z', endUtc: '2030-10-19T10:00:00.000Z', participantUserIds: [],
    };
    await form(imported, [{ offsetMinutes: 10, daysBefore: null, timeOfDayMinutes: null }], [member('me', 'Mia')]);

    expect(screen.queryByTestId('reminders-editor')).toBeNull();
    expect(screen.queryByTestId('participant-picker')).toBeNull();
    await fireEvent.press(screen.getByTestId('edit-task-save'));

    await waitFor(() => expect(mockedUpdateTask).toHaveBeenCalled());
    expect(mockedUpdateTask.mock.calls[0][2].reminders).toBeUndefined();
    expect(mockedUpdateTask.mock.calls[0][2].participantUserIds).toBeUndefined();
  });

  it('a task from Google Tasks has no alerts in Google, so they are the person\'s own: whole days, at a time of day', async () => {
    const googleTask: TaskDto = {
      ...task, isExternal: true, isAllDay: true, isDataIncomplete: true,
      startUtc: '2030-10-18T00:00:00.000Z', deadlineUtc: '2030-10-25T00:00:00.000Z', participantUserIds: [],
    };
    await form(googleTask, []);

    expect(screen.getByTestId('reminder-days-input')).toBeTruthy(); // a whole-day item: days before, not minutes
    await fireEvent.changeText(screen.getByTestId('reminder-days-input'), '1');
    await fireEvent.changeText(screen.getByTestId('reminder-time-input'), '09:00');
    await fireEvent.press(screen.getByTestId('reminder-add'));
    await fireEvent.press(screen.getByTestId('edit-task-save'));

    await waitFor(() => expect(mockedUpdateTask).toHaveBeenCalled());
    expect(mockedUpdateTask.mock.calls[0][2].reminders).toEqual([
      { offsetMinutes: null, daysBefore: 1, timeOfDayMinutes: 540, anchor: ReminderAnchor.Deadline },
    ]);
  });
});
