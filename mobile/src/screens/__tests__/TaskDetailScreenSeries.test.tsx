import { Alert, type AlertButton } from 'react-native';
import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import TaskDetailScreen from '../TaskDetailScreen';
import { deleteTask, getTask, getTeamTasks } from '../../services/taskService';
import { getTeamMembers } from '../../services/teamService';
import { getMyAbsences } from '../../services/userService';
import { resetMyAbsencesCache } from '../../hooks/useMyAbsences';
import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { RecurrenceFrequency, SeriesScope, TaskStatus, TaskType, type TaskDetailDto, type TaskDto } from '../../types/task';
import { TeamRole } from '../../types/team';

jest.mock('../../services/taskService');
jest.mock('../../services/teamService');
jest.mock('../../services/userService');
jest.mock('../../services/authService', () => ({ getCurrentUserId: () => 'me' }));
jest.mock('@react-navigation/native', () => ({
  useNavigation: () => ({ goBack: jest.fn() }),
  useRoute: () => ({ params: { taskId: 't1', teamId: 'team' } }),
}));

const base: TaskDto = {
  id: 't1',
  teamId: 'team',
  assignedUserId: 'me',
  title: 'Weekly review',
  description: null,
  status: TaskStatus.Confirmed,
  deadlineUtc: null,
  impactScore: 5,
  blockedByTaskId: null,
  manualUrgencyOverride: null,
  manualUrgencyOverrideExpiresAtUtc: null,
  currentUrgencyScore: 0.4,
  createdAtUtc: '2026-10-01T00:00:00Z',
  updatedAtUtc: null,
  ...PLAIN_TASK_FIELDS,
  taskType: TaskType.Event,
  startUtc: '2030-10-21T09:00:00.000Z',
  endUtc: '2030-10-21T10:00:00.000Z',
};

const repeating: TaskDto = {
  ...base,
  isRecurring: true,
  recurrenceFrequency: RecurrenceFrequency.Weekly,
  recurrenceInterval: 2,
  recurrenceWeekdays: 1 | 4,
  seriesId: 'series-1',
};

let alertSpy: jest.SpyInstance;
// The pop-up is answered at once with the button named in `answer` (deleting waits for the answer).
let answer = 'All items';

const show = async (task: TaskDto) => {
  (getTask as jest.Mock).mockResolvedValue({ task, latestScoreBreakdown: null } satisfies TaskDetailDto);
  (getTeamTasks as jest.Mock).mockResolvedValue([]);
  (getTeamMembers as jest.Mock).mockResolvedValue([
    { id: 'm1', userId: 'me', userDisplayName: 'Mia', role: TeamRole.Owner, joinedAtUtc: '2026-01-01T00:00:00Z' },
  ]);
  await render(<TaskDetailScreen />);
  await waitFor(() => expect(screen.getByText('Weekly review')).toBeTruthy());
};

beforeEach(() => {
  jest.clearAllMocks();
  resetMyAbsencesCache();
  (getMyAbsences as jest.Mock).mockResolvedValue([]);
  (deleteTask as jest.Mock).mockResolvedValue(undefined);
  answer = 'All items';
  alertSpy = jest.spyOn(Alert, 'alert').mockImplementation((_title, _message, buttons) => {
    buttons?.find((b) => b.text === answer)?.onPress?.();
  });
});

afterEach(() => alertSpy.mockRestore());

// Step 7.5.7: the detail view says how an item repeats, and deleting one of a series asks which occurrences go.
describe('TaskDetailScreen - repeating item', () => {
  it('shows the repeat rule in words, with its end', async () => {
    await show({ ...repeating, recurrenceEndsAtUtc: '2031-01-01T12:00:00.000Z' });

    expect(screen.getByText(/every 2 weeks on Mon, Wed, until/)).toBeTruthy();
  });

  it('asks which occurrences to delete and sends the choice', async () => {
    answer = 'This and following';
    await show(repeating);

    await fireEvent.press(screen.getByTestId('task-delete-button'));

    expect((alertSpy.mock.calls[0][2] as AlertButton[]).map((b) => b.text)).toEqual(expect.arrayContaining(['This item', 'This and following', 'All items']));
    await waitFor(() => expect(deleteTask).toHaveBeenCalledWith('t1', 'team', SeriesScope.ThisAndFollowing));
  });

  it('deletes nothing when the pop-up is cancelled', async () => {
    answer = 'Cancel';
    await show(repeating);

    await fireEvent.press(screen.getByTestId('task-delete-button'));

    expect(alertSpy).toHaveBeenCalled();
    expect(deleteTask).not.toHaveBeenCalled();
  });

  it('keeps the plain confirmation for an item that does not repeat', async () => {
    answer = 'Delete';
    await show(base);

    await fireEvent.press(screen.getByTestId('task-delete-button'));

    expect((alertSpy.mock.calls[0][2] as AlertButton[]).map((b) => b.text)).toEqual(['Cancel', 'Delete']);
    await waitFor(() => expect(deleteTask).toHaveBeenCalledWith('t1', 'team', undefined));
  });
});
