import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import TaskDetailScreen from '../TaskDetailScreen';
import { getTask, getTeamTasks, leaveTask } from '../../services/taskService';
import { getTeamMembers } from '../../services/teamService';
import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { ReminderAnchor, TaskStatus, TaskType, type TaskDetailDto, type TaskDto } from '../../types/task';
import { TeamRole, type TeamMemberDto } from '../../types/team';

jest.mock('../../services/taskService');
jest.mock('../../services/teamService');
jest.mock('../../services/authService', () => ({ getCurrentUserId: () => 'me' }));
jest.mock('@react-navigation/native', () => ({
  useNavigation: () => ({ goBack: jest.fn() }),
  useRoute: () => ({ params: { taskId: 't1', teamId: 'team' } }),
}));

const member = (userId: string, name: string, role = TeamRole.Member): TeamMemberDto => ({ id: `m-${userId}`, userId, userDisplayName: name, role, joinedAtUtc: '2026-01-01T00:00:00Z' });

const base: TaskDto = {
  id: 't1',
  teamId: 'team',
  assignedUserId: 'ann',
  title: 'Sprint retro',
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
  startUtc: '2030-10-20T09:00:00Z',
  endUtc: '2030-10-20T10:00:00Z',
  participantUserIds: ['me', 'ben'],
};

const show = async (detail: TaskDetailDto) => {
  (getTask as jest.Mock).mockResolvedValue(detail);
  (getTeamTasks as jest.Mock).mockResolvedValue([]);
  (getTeamMembers as jest.Mock).mockResolvedValue([member('me', 'Mia'), member('ann', 'Ann', TeamRole.Admin), member('ben', 'Ben')]);
  await render(<TaskDetailScreen />);
  await waitFor(() => expect(screen.getByText('Sprint retro')).toBeTruthy());
};

// Who takes part in the item, its alerts with what they count from, and leaving.
describe('TaskDetailScreen - guests and co-contractors', () => {
  beforeEach(() => jest.clearAllMocks());

  it('names the guests of an event', async () => {
    await show({ task: base, latestScoreBreakdown: null });

    expect(screen.getByText('Guests')).toBeTruthy();
    expect(screen.getByText('Mia, Ben')).toBeTruthy();
  });

  it('calls them co-contractors on a task, and shows nothing when nobody takes part', async () => {
    await show({ task: { ...base, taskType: TaskType.Task, status: TaskStatus.Backlog, deadlineUtc: '2030-10-25T17:00:00Z', startUtc: null, endUtc: null }, latestScoreBreakdown: null });
    expect(screen.getByText('Co-contractors')).toBeTruthy();
  });

  it('a person who is not one of them sees no Leave button', async () => {
    await show({ task: { ...base, participantUserIds: ['ben'] }, latestScoreBreakdown: null });

    expect(screen.queryByTestId('task-leave-button')).toBeNull();
    expect(screen.queryByText('Guests')).toBeTruthy();
  });

  it('nobody listed means no row', async () => {
    await show({ task: { ...base, participantUserIds: [] }, latestScoreBreakdown: null });

    expect(screen.queryByText('Guests')).toBeNull();
    expect(screen.queryByText('Co-contractors')).toBeNull();
  });

  it('a guest can leave the event, and the screen reloads', async () => {
    (leaveTask as jest.Mock).mockResolvedValue(undefined);
    await show({ task: base, latestScoreBreakdown: null });
    expect(screen.getByText('Leave this event')).toBeTruthy();

    await fireEvent.press(screen.getByTestId('task-leave-button'));

    await waitFor(() => expect(leaveTask).toHaveBeenCalledWith('t1', 'team'));
    await waitFor(() => expect(getTask).toHaveBeenCalledTimes(2));
  });

  it('a co-contractor can leave the task, but is not offered to edit it', async () => {
    await show({ task: { ...base, taskType: TaskType.Task, status: TaskStatus.Backlog, deadlineUtc: '2030-10-25T17:00:00Z', startUtc: null, endUtc: null }, latestScoreBreakdown: null });

    expect(screen.getByText('Leave this task')).toBeTruthy();
    expect(screen.queryByTestId('task-edit-button')).toBeNull();
  });

  it('says why a refused leave failed instead of failing silently', async () => {
    (leaveTask as jest.Mock).mockRejectedValue({ status: 404, message: 'You are not taking part in this item.' });
    await show({ task: base, latestScoreBreakdown: null });

    await fireEvent.press(screen.getByTestId('task-leave-button'));

    await waitFor(() => expect(screen.getByText('You are not taking part in this item.')).toBeTruthy());
  });
});

describe('TaskDetailScreen - alerts', () => {
  beforeEach(() => jest.clearAllMocks());

  it('says what a task\'s alerts count from', async () => {
    await show({
      task: { ...base, taskType: TaskType.Task, status: TaskStatus.Backlog, deadlineUtc: '2030-10-25T17:00:00Z', startUtc: '2030-10-22T08:00:00Z', endUtc: null, participantUserIds: [] },
      latestScoreBreakdown: null,
      reminders: [
        { offsetMinutes: 120, daysBefore: null, timeOfDayMinutes: null, anchor: ReminderAnchor.Deadline },
        { offsetMinutes: 30, daysBefore: null, timeOfDayMinutes: null, anchor: ReminderAnchor.Start },
      ],
    });

    expect(screen.getByText(/2 hours before the deadline/)).toBeTruthy();
    expect(screen.getByText(/30 minutes before the start/)).toBeTruthy();
  });

  it('an event\'s alerts keep the short wording', async () => {
    await show({
      task: { ...base, participantUserIds: [] },
      latestScoreBreakdown: null,
      reminders: [{ offsetMinutes: 30, daysBefore: null, timeOfDayMinutes: null, anchor: ReminderAnchor.Start }],
    });

    expect(screen.getByText(/30 minutes before/)).toBeTruthy();
    expect(screen.queryByText(/the start/)).toBeNull();
  });
});
