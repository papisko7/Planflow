import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { CreateTaskModal } from '../CreateTaskModal';
import { createTask } from '../../services/taskService';
import { getTeamMembers } from '../../services/teamService';
import { EventTemplate, TaskType } from '../../types/task';
import type { TeamDto } from '../../types/team';

jest.mock('../../services/taskService');
jest.mock('../../services/teamService');
jest.mock('../../services/authService', () => ({ getCurrentUserId: () => 'me' }));

const mockedCreateTask = createTask as jest.MockedFunction<typeof createTask>;
const mockedGetTeamMembers = getTeamMembers as jest.MockedFunction<typeof getTeamMembers>;

const personal: TeamDto = { id: 'p', name: 'Me', description: null, memberCount: 1, isPersonal: true, muteNotifications: false, muteChat: false };

const open = () => render(<CreateTaskModal visible teams={[personal]} tasks={[]} onClose={jest.fn()} onCreated={jest.fn()} />);

// Pick the day a week from now as the start (the picker's quick choice) and confirm it.
async function pickStartInAWeek() {
  await fireEvent.press(screen.getByTestId('event-start-picker-button'));
  await fireEvent.press(screen.getByTestId('event-start-picker-quick-week'));
  await fireEvent.press(screen.getByTestId('event-start-picker-set'));
}

// Step 7.5.5: the template chips of the form and the fields each template shows.
describe('CreateTaskModal - templates', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedGetTeamMembers.mockResolvedValue([]);
    mockedCreateTask.mockResolvedValue({} as never);
  });

  it('offers a chip for the Task, the plain Event and each of the four templates', async () => {
    await open();

    for (const id of ['schedule-type-task', 'schedule-type-event', 'schedule-template-out-of-office', 'schedule-template-focus-time', 'schedule-template-working-location', 'schedule-template-birthday']) {
      expect(screen.getByTestId(id)).toBeTruthy();
    }
  });

  it('a plain Event shows place, busy/free, colour and description; a Task shows colour and description only', async () => {
    await open();
    expect(screen.queryByTestId('schedule-location-input')).toBeNull();
    expect(screen.queryByTestId('schedule-busy')).toBeNull();
    expect(screen.getByTestId('color-swatch-tomato')).toBeTruthy();
    expect(screen.getByTestId('create-task-description-input')).toBeTruthy();

    await fireEvent.press(screen.getByTestId('schedule-type-event'));

    expect(screen.getByTestId('schedule-location-input')).toBeTruthy();
    expect(screen.getByTestId('schedule-busy')).toBeTruthy();
    expect(screen.getByTestId('schedule-free')).toBeTruthy();
  });

  it('sends the description, place, colour and "free" of a plain Event', async () => {
    await open();
    await fireEvent.press(screen.getByTestId('schedule-type-event'));
    await fireEvent.changeText(screen.getByTestId('create-task-title-input'), 'Dentist');
    await fireEvent.changeText(screen.getByTestId('create-task-description-input'), 'Bring the X-ray');
    await fireEvent.changeText(screen.getByTestId('schedule-location-input'), 'Clinic');
    await fireEvent.press(screen.getByTestId('color-swatch-peacock'));
    await fireEvent.press(screen.getByTestId('schedule-free'));
    await pickStartInAWeek();
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));

    await waitFor(() => expect(mockedCreateTask).toHaveBeenCalled());
    expect(mockedCreateTask.mock.calls[0][1]).toMatchObject({
      taskType: TaskType.Event,
      eventTemplate: EventTemplate.Default,
      description: 'Bring the X-ray',
      location: 'Clinic',
      colorKey: 'peacock',
      isBusy: false,
    });
  });

  it('Out of office: no description, place or PlanFlow section; the title defaults to the template name', async () => {
    await open();
    await fireEvent.press(screen.getByTestId('schedule-template-out-of-office'));

    expect(screen.queryByTestId('create-task-description-input')).toBeNull();
    expect(screen.queryByTestId('schedule-location-input')).toBeNull();
    expect(screen.queryByTestId('create-task-impact-input')).toBeNull();
    expect(screen.queryByText('PlanFlow')).toBeNull();
    expect(screen.getByTestId('color-swatch-tomato')).toBeTruthy();
    expect(screen.getByPlaceholderText('Out of office')).toBeTruthy();

    await pickStartInAWeek(); // leave the title empty
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));

    await waitFor(() => expect(mockedCreateTask).toHaveBeenCalled());
    expect(mockedCreateTask.mock.calls[0][1]).toMatchObject({
      title: 'Out of office',
      eventTemplate: EventTemplate.OutOfOffice,
      description: null,
      impactScore: null,
      blockedByTaskId: null,
    });
  });

  it('Out of office can be all-day, and then it is stored as whole days', async () => {
    await open();
    await fireEvent.press(screen.getByTestId('schedule-template-out-of-office'));
    await fireEvent(screen.getByTestId('schedule-allday-switch'), 'valueChange', true);
    await pickStartInAWeek();
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));

    await waitFor(() => expect(mockedCreateTask).toHaveBeenCalled());
    const { isAllDay, startUtc, endUtc } = mockedCreateTask.mock.calls[0][1];
    expect(isAllDay).toBe(true);
    expect(startUtc!.endsWith('T00:00:00.000Z')).toBe(true);
    expect(new Date(endUtc!).getTime() - new Date(startUtc!).getTime()).toBe(24 * 3_600_000);
  });

  it('Focus time: a place and colour, always busy, never an all-day switch', async () => {
    await open();
    await fireEvent.press(screen.getByTestId('schedule-template-focus-time'));

    expect(screen.queryByTestId('schedule-allday-switch')).toBeNull();
    expect(screen.queryByTestId('schedule-busy')).toBeNull();
    expect(screen.getByTestId('schedule-location-input')).toBeTruthy();

    await fireEvent.changeText(screen.getByTestId('schedule-location-input'), 'Library');
    await pickStartInAWeek();
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));

    await waitFor(() => expect(mockedCreateTask).toHaveBeenCalled());
    expect(mockedCreateTask.mock.calls[0][1]).toMatchObject({
      title: 'Focus time', eventTemplate: EventTemplate.FocusTime, location: 'Library', isBusy: true, isAllDay: false,
    });
  });

  it('Working location: Home by default, Office with one tap, or a label of your own; no colour', async () => {
    await open();
    await fireEvent.press(screen.getByTestId('schedule-template-working-location'));
    expect(screen.queryByTestId('color-swatch-tomato')).toBeNull();
    expect(screen.queryByTestId('schedule-location-input')).toBeNull();

    await pickStartInAWeek();
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));
    await waitFor(() => expect(mockedCreateTask).toHaveBeenCalledTimes(1));
    expect(mockedCreateTask.mock.calls[0][1]).toMatchObject({ eventTemplate: EventTemplate.WorkingLocation, location: 'Home', colorKey: null });

    await fireEvent.press(screen.getByTestId('schedule-working-office'));
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));
    await waitFor(() => expect(mockedCreateTask).toHaveBeenCalledTimes(2));
    expect(mockedCreateTask.mock.calls[1][1].location).toBe('Office');

    await fireEvent.press(screen.getByTestId('schedule-working-other'));
    await fireEvent.changeText(screen.getByTestId('schedule-location-input'), 'Client site');
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));
    await waitFor(() => expect(mockedCreateTask).toHaveBeenCalledTimes(3));
    expect(mockedCreateTask.mock.calls[2][1].location).toBe('Client site');
  });

  it('Birthday: a single date, always all-day, the name is required', async () => {
    await open();
    await fireEvent.press(screen.getByTestId('schedule-template-birthday'));

    expect(screen.getByPlaceholderText('Name')).toBeTruthy();
    expect(screen.queryByTestId('schedule-allday-switch')).toBeNull();
    expect(screen.queryByTestId('event-end-picker-button')).toBeNull();

    await pickStartInAWeek();
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));
    expect(await screen.findByText('Title is required.')).toBeTruthy();
    expect(mockedCreateTask).not.toHaveBeenCalled();

    await fireEvent.changeText(screen.getByTestId('create-task-title-input'), 'Anna');
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));
    await waitFor(() => expect(mockedCreateTask).toHaveBeenCalled());
    const request = mockedCreateTask.mock.calls[0][1];
    expect(request).toMatchObject({ title: 'Anna', eventTemplate: EventTemplate.Birthday, isAllDay: true });
    expect(new Date(request.endUtc!).getTime() - new Date(request.startUtc!).getTime()).toBe(24 * 3_600_000);
  });

  it('switching the chip clears what the new kind does not have', async () => {
    await open();
    await fireEvent.press(screen.getByTestId('schedule-type-event'));
    await fireEvent.changeText(screen.getByTestId('schedule-location-input'), 'Clinic');
    await fireEvent.press(screen.getByTestId('color-swatch-grape'));

    await fireEvent.press(screen.getByTestId('schedule-template-out-of-office'));
    await pickStartInAWeek();
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));

    await waitFor(() => expect(mockedCreateTask).toHaveBeenCalled());
    expect(mockedCreateTask.mock.calls[0][1]).toMatchObject({ location: null, colorKey: 'grape' });
  });

  it('a Task can have a colour and a description, and never a place or an all-day flag', async () => {
    await open();
    await fireEvent.changeText(screen.getByTestId('create-task-title-input'), 'Write chapter');
    await fireEvent.changeText(screen.getByTestId('create-task-description-input'), 'Sections 2 and 3');
    await fireEvent.press(screen.getByTestId('color-swatch-basil'));
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));

    await waitFor(() => expect(mockedCreateTask).toHaveBeenCalled());
    expect(mockedCreateTask.mock.calls[0][1]).toMatchObject({
      taskType: TaskType.Task, eventTemplate: EventTemplate.Default, description: 'Sections 2 and 3', colorKey: 'basil', location: null, isAllDay: false,
    });
  });

  it('shows the PlanFlow section (impact) for a Task and a plain Event', async () => {
    await open();
    expect(screen.getByText('PlanFlow')).toBeTruthy();
    expect(screen.getByTestId('create-task-impact-input')).toBeTruthy();

    await fireEvent.press(screen.getByTestId('schedule-type-event'));
    expect(screen.getByText('PlanFlow')).toBeTruthy();
  });
});
