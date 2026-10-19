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

// The Task vs Event dimorphism of the unified CalendarItem form (step 7.3).
describe('CreateTaskModal - Task vs Event', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedGetTeamMembers.mockResolvedValue([]);
  });

  it('sends a plain Task by default with effort converted to hours', async () => {
    mockedCreateTask.mockResolvedValue({} as never);

    await render(<CreateTaskModal visible teams={[personal]} tasks={[]} onClose={jest.fn()} onCreated={jest.fn()} />);
    await fireEvent.changeText(screen.getByTestId('create-task-title-input'), 'Write thesis chapter');
    await fireEvent.changeText(screen.getByTestId('effort-input'), '2');
    await fireEvent.press(screen.getByTestId('effort-unit-day'));
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));

    await waitFor(() =>
      expect(mockedCreateTask).toHaveBeenCalledWith(
        'p',
        expect.objectContaining({ taskType: 0, estimatedEffortHours: 48, startUtc: null, endUtc: null, isRecurring: false }),
      ),
    );
  });

  it('shows start/end instead of deadline/effort for an Event and requires a start', async () => {
    await render(<CreateTaskModal visible teams={[personal]} tasks={[]} onClose={jest.fn()} onCreated={jest.fn()} />);
    expect(screen.getByTestId('deadline-picker-button')).toBeTruthy();
    expect(screen.getByTestId('effort-input')).toBeTruthy();

    await fireEvent.press(screen.getByTestId('schedule-type-event'));

    expect(screen.queryByTestId('deadline-picker-button')).toBeNull();
    expect(screen.queryByTestId('effort-input')).toBeNull();
    expect(screen.getByTestId('event-start-picker-button')).toBeTruthy();
    expect(screen.getByTestId('event-end-picker-button')).toBeTruthy();

    await fireEvent.changeText(screen.getByTestId('create-task-title-input'), 'Dentist');
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));

    expect(await screen.findByText('An event needs a start time.')).toBeTruthy();
    expect(mockedCreateTask).not.toHaveBeenCalled();
  });

  it('fills the end with start + 1h when only the day is picked (time defaults to 09:00)', async () => {
    mockedCreateTask.mockResolvedValue({} as never);

    await render(<CreateTaskModal visible teams={[personal]} tasks={[]} onClose={jest.fn()} onCreated={jest.fn()} />);
    await fireEvent.press(screen.getByTestId('schedule-type-event'));
    await fireEvent.changeText(screen.getByTestId('create-task-title-input'), 'Dentist');

    await fireEvent.press(screen.getByTestId('event-start-picker-button'));
    await fireEvent.press(screen.getByTestId('event-start-picker-quick-week'));
    await fireEvent.press(screen.getByTestId('event-start-picker-set'));
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));

    await waitFor(() => expect(mockedCreateTask).toHaveBeenCalled());
    const { startUtc, endUtc } = mockedCreateTask.mock.calls[0][1];
    expect(new Date(startUtc!).getHours()).toBe(9);
    expect(new Date(endUtc!).getTime() - new Date(startUtc!).getTime()).toBe(3_600_000);
  });

  it('fills the start with end - 1h when the end is picked first, and the end reads as set (not optional)', async () => {
    mockedCreateTask.mockResolvedValue({} as never);

    await render(<CreateTaskModal visible teams={[personal]} tasks={[]} onClose={jest.fn()} onCreated={jest.fn()} />);
    await fireEvent.press(screen.getByTestId('schedule-type-event'));
    await fireEvent.changeText(screen.getByTestId('create-task-title-input'), 'Workshop');
    expect(screen.queryByText(/optional/i)).toBeNull(); // only "Estimated effort (optional)" lives on the Task form

    await fireEvent.press(screen.getByTestId('event-end-picker-button'));
    await fireEvent.press(screen.getByTestId('event-end-picker-quick-week'));
    await fireEvent.press(screen.getByTestId('event-end-picker-time-1500'));
    await fireEvent.press(screen.getByTestId('event-end-picker-set'));

    expect(screen.getByText(/Start: .*14:00/)).toBeTruthy();
    expect(screen.getByText(/End: .*15:00/)).toBeTruthy();

    await fireEvent.press(screen.getByTestId('create-task-submit-button'));
    await waitFor(() => expect(mockedCreateTask).toHaveBeenCalled());
    const { startUtc, endUtc } = mockedCreateTask.mock.calls[0][1];
    expect(new Date(endUtc!).getTime() - new Date(startUtc!).getTime()).toBe(3_600_000);
  });

  it('all-day: picking the start day fills the end with that same day, and no clock times are offered', async () => {
    mockedCreateTask.mockResolvedValue({} as never);

    await render(<CreateTaskModal visible teams={[personal]} tasks={[]} onClose={jest.fn()} onCreated={jest.fn()} />);
    await fireEvent.press(screen.getByTestId('schedule-type-event'));
    await fireEvent.changeText(screen.getByTestId('create-task-title-input'), 'Conference');
    await fireEvent(screen.getByTestId('schedule-allday-switch'), 'valueChange', true);

    await fireEvent.press(screen.getByTestId('event-start-picker-button'));
    expect(screen.queryByTestId('event-start-picker-time-1500')).toBeNull();
    await fireEvent.press(screen.getByTestId('event-start-picker-quick-week'));
    await fireEvent.press(screen.getByTestId('event-start-picker-set'));

    expect(screen.getByText(/End: /)).toBeTruthy(); // the end shows up by itself
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));
    await waitFor(() => expect(mockedCreateTask).toHaveBeenCalled());
    const { startUtc, endUtc } = mockedCreateTask.mock.calls[0][1];
    // Stored as whole days: midnight UTC of the picked day and the midnight after it, flagged all-day.
    const start = new Date(startUtc!);
    const end = new Date(endUtc!);
    expect(mockedCreateTask.mock.calls[0][1].isAllDay).toBe(true);
    expect([start.getUTCHours(), start.getUTCMinutes()]).toEqual([0, 0]);
    expect(end.getTime() - start.getTime()).toBe(24 * 3_600_000);
  });

  it('creates an Event with its start and a recurrence interval in hours', async () => {
    mockedCreateTask.mockResolvedValue({} as never);

    await render(<CreateTaskModal visible teams={[personal]} tasks={[]} onClose={jest.fn()} onCreated={jest.fn()} />);
    await fireEvent.press(screen.getByTestId('schedule-type-event'));
    await fireEvent.changeText(screen.getByTestId('create-task-title-input'), 'Team sync');

    await fireEvent.press(screen.getByTestId('event-start-picker-button'));
    await fireEvent.press(screen.getByTestId('event-start-picker-quick-week'));
    await fireEvent.press(screen.getByTestId('event-start-picker-time-1500'));
    await fireEvent.press(screen.getByTestId('event-start-picker-set'));

    await fireEvent(screen.getByTestId('schedule-recurring-switch'), 'valueChange', true);
    await fireEvent.press(screen.getByTestId('recurrence-frequency-1')); // weekly
    await fireEvent.changeText(screen.getByTestId('recurrence-interval-input'), '2');
    await fireEvent.press(screen.getByTestId('create-task-submit-button'));

    await waitFor(() => expect(mockedCreateTask).toHaveBeenCalled());
    const request = mockedCreateTask.mock.calls[0][1];
    expect(request.taskType).toBe(1);
    expect(request.deadlineUtc).toBeNull();
    expect(request.startUtc).not.toBeNull();
    expect(request.isRecurring).toBe(true);
    expect(request.recurrenceFrequency).toBe(1);
    expect(request.recurrenceInterval).toBe(2);
    expect(request.recurrenceWeekdays).not.toBeNull(); // the weekday of the first occurrence is pre-selected
  });
});
