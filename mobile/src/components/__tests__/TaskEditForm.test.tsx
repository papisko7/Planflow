import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { TaskEditForm } from '../TaskEditForm';
import { updateTask } from '../../services/taskService';
import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { EventTemplate, TaskStatus, TaskType, type TaskDto } from '../../types/task';

jest.mock('../../services/taskService');

const mockedUpdateTask = updateTask as jest.MockedFunction<typeof updateTask>;

const base: TaskDto = {
  id: 't1',
  teamId: 'team1',
  assignedUserId: 'me',
  title: 'Standup',
  description: 'Daily',
  status: TaskStatus.Confirmed,
  deadlineUtc: null,
  impactScore: null,
  blockedByTaskId: null,
  manualUrgencyOverride: null,
  manualUrgencyOverrideExpiresAtUtc: null,
  currentUrgencyScore: 0.3,
  createdAtUtc: '2026-10-01T00:00:00Z',
  updatedAtUtc: null,
  ...PLAIN_TASK_FIELDS,
  taskType: TaskType.Event,
  startUtc: '2030-10-19T09:00:00.000Z',
  endUtc: '2030-10-19T10:00:00.000Z',
};

const form = (task: TaskDto) =>
  render(<TaskEditForm task={task} members={[]} canAssign={false} onSaved={jest.fn()} onCancel={jest.fn()} />);

// The edit form follows the item's template, and an item imported from Google keeps Google's fields read-only.
describe('TaskEditForm', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedUpdateTask.mockResolvedValue({} as never);
  });

  it('opens an event with its place and colour and saves a changed one', async () => {
    await form({ ...base, location: 'Room 4', colorKey: 'sage', isBusy: false });

    expect(screen.getByTestId('schedule-location-input').props.value).toBe('Room 4');
    expect(screen.getByText('Colour: Sage')).toBeTruthy();

    await fireEvent.changeText(screen.getByTestId('schedule-location-input'), 'Room 5');
    await fireEvent.press(screen.getByTestId('color-swatch-tomato'));
    await fireEvent.press(screen.getByTestId('edit-task-save'));

    await waitFor(() => expect(mockedUpdateTask).toHaveBeenCalled());
    expect(mockedUpdateTask.mock.calls[0][2]).toMatchObject({ location: 'Room 5', colorKey: 'tomato', isBusy: false, eventTemplate: EventTemplate.Default });
  });

  it('an Out of office item has no description, no place and no PlanFlow section', async () => {
    await form({ ...base, eventTemplate: EventTemplate.OutOfOffice, description: null });

    expect(screen.queryByTestId('edit-task-description-input')).toBeNull();
    expect(screen.queryByTestId('schedule-location-input')).toBeNull();
    expect(screen.queryByTestId('edit-task-impact-input')).toBeNull();
    expect(screen.queryByText('PlanFlow')).toBeNull();
  });

  it('shows the "please check" banner for an item that needs details, and not otherwise', async () => {
    const { unmount } = await form({ ...base, isDataIncomplete: true });
    expect(screen.getByTestId('edit-task-verify-banner')).toBeTruthy();
    await unmount();

    await form(base);
    expect(screen.queryByTestId('edit-task-verify-banner')).toBeNull();
  });

  it('an item imported from Google shows its title read-only and offers only the PlanFlow fields', async () => {
    await form({ ...base, isExternal: true, isDataIncomplete: true, title: 'Team offsite', location: 'Lisbon' });

    expect(screen.getByTestId('edit-task-imported-notice')).toBeTruthy();
    expect(screen.getByText('Team offsite')).toBeTruthy();
    expect(screen.queryByTestId('edit-task-title-input')).toBeNull();
    expect(screen.queryByTestId('edit-task-description-input')).toBeNull();
    expect(screen.queryByTestId('schedule-type-event')).toBeNull();
    expect(screen.queryByTestId('schedule-location-input')).toBeNull();
    expect(screen.queryByTestId('color-swatch-tomato')).toBeNull();
    expect(screen.getByTestId('edit-task-impact-input')).toBeTruthy();
    expect(screen.getByTestId('edit-task-status-6')).toBeTruthy();
  });

  it('saving an imported item resends Google\'s fields untouched and changes only impact and status', async () => {
    const imported: TaskDto = {
      ...base, isExternal: true, isAllDay: true, startUtc: '2030-10-20T00:00:00.000Z', endUtc: '2030-10-22T00:00:00.000Z',
      title: 'Team offsite', description: 'Agenda', location: 'Lisbon', colorKey: 'sage', isBusy: false,
    };
    await form(imported);

    await fireEvent.changeText(screen.getByTestId('edit-task-impact-input'), '8');
    await fireEvent.press(screen.getByTestId('edit-task-status-6'));
    await fireEvent.press(screen.getByTestId('edit-task-save'));

    await waitFor(() => expect(mockedUpdateTask).toHaveBeenCalled());
    expect(mockedUpdateTask.mock.calls[0][2]).toMatchObject({
      title: 'Team offsite', description: 'Agenda', location: 'Lisbon', colorKey: 'sage', isBusy: false, isAllDay: true,
      startUtc: '2030-10-20T00:00:00.000Z', endUtc: '2030-10-22T00:00:00.000Z',
      impactScore: 8, status: TaskStatus.Tentative,
    });
  });

  it('an older all-day event is saved back as whole days in the new convention', async () => {
    await form({
      ...base, isAllDay: false,
      startUtc: new Date(2030, 9, 20, 0, 0).toISOString(), endUtc: new Date(2030, 9, 20, 23, 59).toISOString(),
    });

    expect((screen.getByTestId('schedule-allday-switch').props as { value: boolean }).value).toBe(true);
    await fireEvent.press(screen.getByTestId('edit-task-save'));

    await waitFor(() => expect(mockedUpdateTask).toHaveBeenCalled());
    expect(mockedUpdateTask.mock.calls[0][2]).toMatchObject({
      isAllDay: true, startUtc: '2030-10-20T00:00:00.000Z', endUtc: '2030-10-21T00:00:00.000Z',
    });
  });
});

// A task imported from Google Tasks. Google owns its title, notes and due day; the user plans it (start, effort, impact).
describe('TaskEditForm - task from Google Tasks', () => {
  const googleTask: TaskDto = {
    ...base,
    taskType: TaskType.Task,
    status: TaskStatus.Backlog,
    title: 'Write chapter 3',
    description: 'Check the sources',
    isExternal: true,
    isAllDay: true,
    isDataIncomplete: true,
    startUtc: '2030-10-18T00:00:00.000Z',
    endUtc: null,
    deadlineUtc: '2030-10-25T00:00:00.000Z',
    googleTaskListId: 'l1',
    googleTaskListTitle: 'My Tasks',
  };
  const tomorrowAsWireDay = () => {
    const now = new Date();
    return new Date(Date.UTC(now.getFullYear(), now.getMonth(), now.getDate() + 1)).toISOString();
  };

  beforeEach(() => {
    jest.clearAllMocks();
    mockedUpdateTask.mockResolvedValue({} as never);
  });

  it('explains it comes from Google Tasks, shows its title read-only and offers start, effort and impact', async () => {
    await form(googleTask);

    expect(screen.getByText(/Imported from Google Tasks/)).toBeTruthy();
    expect(screen.getByText('Write chapter 3')).toBeTruthy();
    expect(screen.queryByTestId('edit-task-title-input')).toBeNull();
    expect(screen.queryByTestId('edit-task-description-input')).toBeNull();
    expect(screen.getByTestId('task-start-picker-button')).toBeTruthy();
    expect(screen.getByTestId('effort-input')).toBeTruthy();
    expect(screen.getByTestId('edit-task-impact-input')).toBeTruthy();
    expect(screen.queryByTestId('deadline-picker-button')).toBeNull(); // the due day is Google's
  });

  it('saving resends the stored days untouched and the effort and impact the user typed', async () => {
    await form(googleTask);

    await fireEvent.changeText(screen.getByTestId('effort-input'), '3');
    await fireEvent.changeText(screen.getByTestId('edit-task-impact-input'), '7');
    await fireEvent.press(screen.getByTestId('edit-task-save'));

    await waitFor(() => expect(mockedUpdateTask).toHaveBeenCalled());
    expect(mockedUpdateTask.mock.calls[0][2]).toMatchObject({
      taskType: TaskType.Task,
      startUtc: '2030-10-18T00:00:00.000Z', // a whole day, stored as midnight UTC whatever the phone's zone
      deadlineUtc: '2030-10-25T00:00:00.000Z',
      estimatedEffortHours: 3,
      impactScore: 7,
      isAllDay: true,
    });
  });

  it('a new start is sent as the picked day at midnight UTC', async () => {
    await form({ ...googleTask, startUtc: null });

    await fireEvent.press(screen.getByTestId('task-start-picker-button'));
    await fireEvent.press(screen.getByTestId('task-start-picker-quick-tomorrow'));
    await fireEvent.press(screen.getByTestId('task-start-picker-set'));
    await fireEvent.press(screen.getByTestId('edit-task-save'));

    await waitFor(() => expect(mockedUpdateTask).toHaveBeenCalled());
    expect(mockedUpdateTask.mock.calls[0][2]).toMatchObject({ startUtc: tomorrowAsWireDay() });
  });

  it('refuses a start after the due day and does not call the server', async () => {
    await form({ ...googleTask, startUtc: '2030-11-01T00:00:00.000Z' });

    await fireEvent.press(screen.getByTestId('edit-task-save'));

    expect(screen.getByText('The start must not be after the deadline.')).toBeTruthy();
    expect(mockedUpdateTask).not.toHaveBeenCalled();
  });

  it('refuses an effort of zero', async () => {
    await form(googleTask);

    await fireEvent.changeText(screen.getByTestId('effort-input'), '0');
    await fireEvent.press(screen.getByTestId('edit-task-save'));

    expect(screen.getByText(/Effort must be greater than 0/)).toBeTruthy();
    expect(mockedUpdateTask).not.toHaveBeenCalled();
  });

  it('a task with no due day in Google lets the user set the placeholder deadline', async () => {
    await form({ ...googleTask, isDeadlineDefaulted: true, startUtc: null, deadlineUtc: '2030-10-19T00:00:00.000Z' });

    await fireEvent.press(screen.getByTestId('deadline-picker-button'));
    await fireEvent.press(screen.getByTestId('deadline-picker-quick-tomorrow'));
    await fireEvent.press(screen.getByTestId('deadline-picker-set'));
    await fireEvent.press(screen.getByTestId('edit-task-save'));

    await waitFor(() => expect(mockedUpdateTask).toHaveBeenCalled());
    expect(mockedUpdateTask.mock.calls[0][2]).toMatchObject({ deadlineUtc: tomorrowAsWireDay() });
  });

  it('an imported calendar event can get an effort estimate too, and keeps its Google schedule', async () => {
    await form({ ...base, isExternal: true, isDataIncomplete: true });

    expect(screen.queryByTestId('task-start-picker-button')).toBeNull(); // events have no planned start of their own
    await fireEvent.changeText(screen.getByTestId('effort-input'), '2');
    await fireEvent.press(screen.getByTestId('edit-task-save'));

    await waitFor(() => expect(mockedUpdateTask).toHaveBeenCalled());
    expect(mockedUpdateTask.mock.calls[0][2]).toMatchObject({
      estimatedEffortHours: 2, startUtc: '2030-10-19T09:00:00.000Z', endUtc: '2030-10-19T10:00:00.000Z',
    });
  });
});
