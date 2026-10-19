import { Alert, type AlertButton } from 'react-native';
import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { TaskEditForm } from '../TaskEditForm';
import { updateTask } from '../../services/taskService';
import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { RecurrenceFrequency, SeriesScope, TaskStatus, TaskType, type TaskDto } from '../../types/task';

jest.mock('../../services/taskService');

const mockedUpdateTask = updateTask as jest.MockedFunction<typeof updateTask>;

const base: TaskDto = {
  id: 't1',
  teamId: 'team1',
  assignedUserId: 'me',
  title: 'Standup',
  description: null,
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
  startUtc: '2030-10-21T09:00:00.000Z', // a Monday
  endUtc: '2030-10-21T10:00:00.000Z',
  isRecurring: true,
  recurrenceFrequency: RecurrenceFrequency.Weekly,
  recurrenceInterval: 1,
  recurrenceWeekdays: 1,
  seriesId: 'series-1',
};

const notRepeating: Partial<TaskDto> = {
  isRecurring: false,
  recurrenceFrequency: null,
  recurrenceInterval: null,
  recurrenceWeekdays: null,
  seriesId: null,
};

let alertSpy: jest.SpyInstance;
// The pop-up is answered at once with the button named in `answer`; saving waits for that answer, so it cannot be left open.
let answer = 'All items';
const popup = () => alertSpy.mock.calls[0] as [string, string, AlertButton[]];

const form = (task: TaskDto) =>
  render(<TaskEditForm task={task} members={[]} canAssign={false} onSaved={jest.fn()} onCancel={jest.fn()} />);

// Saving a change of an item that repeats asks which occurrences it applies to.
describe('TaskEditForm - repeating item', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedUpdateTask.mockResolvedValue({} as never);
    answer = 'All items';
    alertSpy = jest.spyOn(Alert, 'alert').mockImplementation((_title, _message, buttons) => {
      buttons?.find((b) => b.text === answer)?.onPress?.();
    });
  });

  afterEach(() => alertSpy.mockRestore());

  it('asks "this / this and following / all" before saving, and sends the choice', async () => {
    answer = 'This and following';
    await form(base);
    await fireEvent.changeText(screen.getByTestId('edit-task-title-input'), 'Daily sync');
    await fireEvent.press(screen.getByTestId('edit-task-save'));

    expect(popup()[0]).toBe('Edit repeating item');
    expect(popup()[2].map((b) => b.text)).toEqual(expect.arrayContaining(['This item', 'This and following', 'All items']));
    await waitFor(() => expect(mockedUpdateTask).toHaveBeenCalled());
    expect(mockedUpdateTask.mock.calls[0][2]).toMatchObject({ title: 'Daily sync', scope: SeriesScope.ThisAndFollowing });
  });

  it('saves nothing when the pop-up is cancelled', async () => {
    answer = 'Cancel';
    await form(base);
    await fireEvent.press(screen.getByTestId('edit-task-save'));

    expect(alertSpy).toHaveBeenCalled();
    expect(mockedUpdateTask).not.toHaveBeenCalled();
  });

  it('sends the repeat rule as it was edited: an interval and the days of the week', async () => {
    await form(base);
    await fireEvent.press(screen.getByTestId('recurrence-day-wed'));
    await fireEvent.changeText(screen.getByTestId('recurrence-interval-input'), '2');
    await fireEvent.press(screen.getByTestId('edit-task-save'));

    await waitFor(() => expect(mockedUpdateTask).toHaveBeenCalled());
    expect(mockedUpdateTask.mock.calls[0][2]).toMatchObject({
      isRecurring: true,
      recurrenceFrequency: RecurrenceFrequency.Weekly,
      recurrenceInterval: 2,
      recurrenceWeekdays: 1 | 4,
      scope: SeriesScope.All,
    });
  });

  it('says that a changed rule does not apply to one occurrence alone', async () => {
    answer = 'Cancel';
    await form(base);
    await fireEvent.press(screen.getByTestId('recurrence-frequency-0')); // weekly -> daily
    await fireEvent.press(screen.getByTestId('edit-task-save'));

    expect(popup()[1]).toMatch(/changed how it repeats/i);
  });

  it('does not ask for an item that does not repeat, and sends no scope', async () => {
    await form({ ...base, ...notRepeating });
    await fireEvent.press(screen.getByTestId('edit-task-save'));

    await waitFor(() => expect(mockedUpdateTask).toHaveBeenCalled());
    expect(alertSpy).not.toHaveBeenCalled();
    expect(mockedUpdateTask.mock.calls[0][2]).not.toHaveProperty('scope');
  });

  it('refuses a weekly rule with no day chosen', async () => {
    await form(base);
    await fireEvent.press(screen.getByTestId('recurrence-day-mon')); // the only chosen day, now off
    await fireEvent.press(screen.getByTestId('edit-task-save'));

    expect(screen.getByText(/at least one day of the week/i)).toBeTruthy();
    expect(alertSpy).not.toHaveBeenCalled();
    expect(mockedUpdateTask).not.toHaveBeenCalled();
  });

  it('also asks for an item imported from a Google series, whose PlanFlow fields it passes on', async () => {
    await form({ ...base, ...notRepeating, isExternal: true, seriesId: 'g-1' });
    await fireEvent.changeText(screen.getByTestId('edit-task-impact-input'), '7');
    await fireEvent.press(screen.getByTestId('edit-task-save'));

    await waitFor(() => expect(mockedUpdateTask).toHaveBeenCalled());
    expect(mockedUpdateTask.mock.calls[0][2]).toMatchObject({ impactScore: 7, scope: SeriesScope.All });
  });
});
