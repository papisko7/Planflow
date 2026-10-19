import { render, screen, waitFor } from '@testing-library/react-native';
import TaskDetailScreen from '../TaskDetailScreen';
import { getTask, getTeamTasks } from '../../services/taskService';
import { getTeamMembers } from '../../services/teamService';
import { getMyAbsences } from '../../services/userService';
import { resetMyAbsencesCache } from '../../hooks/useMyAbsences';
import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { TaskStatus, type TaskDetailDto, type TaskDto } from '../../types/task';
import { TeamRole, type TeamMemberDto } from '../../types/team';

jest.mock('../../services/taskService');
jest.mock('../../services/teamService');
jest.mock('../../services/userService');
jest.mock('../../services/authService', () => ({ getCurrentUserId: () => 'me' }));
jest.mock('@react-navigation/native', () => ({
  useNavigation: () => ({ goBack: jest.fn() }),
  useRoute: () => ({ params: { taskId: 't1', teamId: 'team' } }),
}));

const member = (userId: string, name: string, role: TeamRole): TeamMemberDto => ({ id: `m-${userId}`, userId, userDisplayName: name, role, joinedAtUtc: '2026-01-01T00:00:00Z' });
const inDays = (days: number) => new Date(Date.now() + days * 86_400_000).toISOString();

const base: TaskDto = {
  id: 't1',
  teamId: 'team',
  assignedUserId: 'me',
  title: 'Quarterly report',
  description: null,
  status: TaskStatus.Backlog,
  deadlineUtc: inDays(3),
  impactScore: 5,
  blockedByTaskId: null,
  manualUrgencyOverride: null,
  manualUrgencyOverrideExpiresAtUtc: null,
  currentUrgencyScore: 0.4,
  createdAtUtc: '2026-10-01T00:00:00Z',
  updatedAtUtc: null,
  ...PLAIN_TASK_FIELDS,
};

const show = async (task: TaskDto, myRole: TeamRole = TeamRole.Member) => {
  (getTask as jest.Mock).mockResolvedValue({ task, latestScoreBreakdown: null } satisfies TaskDetailDto);
  (getTeamTasks as jest.Mock).mockResolvedValue([]);
  (getTeamMembers as jest.Mock).mockResolvedValue([member('me', 'Mia', myRole), member('boss', 'Olga', TeamRole.Owner)]);
  await render(<TaskDetailScreen />);
  await waitFor(() => expect(screen.getByText('Quarterly report')).toBeTruthy());
};

beforeEach(() => {
  jest.clearAllMocks();
  resetMyAbsencesCache();
  (getMyAbsences as jest.Mock).mockResolvedValue([]);
});

// An assignment made by an Owner/Admin is locked to the assignee; the details say so and say who assigned it.
describe('TaskDetailScreen - assignment lock', () => {
  it('lets the assignee give back a task they took themselves', async () => {
    await show(base);

    expect(screen.getByTestId('task-abandon-button')).toBeTruthy();
    expect(screen.queryByTestId('task-locked-note')).toBeNull();
  });

  it('replaces "Abandon" with a note on a locked task, and says who assigned it', async () => {
    await show({ ...base, isAssignmentLocked: true, assignedByUserId: 'boss' });

    expect(screen.queryByTestId('task-abandon-button')).toBeNull();
    expect(screen.getByTestId('task-locked-note')).toBeTruthy();
    expect(screen.getByText('Mia (assigned by Olga)')).toBeTruthy();
  });

  it('keeps "Abandon" for an Owner/Admin even on a locked task', async () => {
    await show({ ...base, isAssignmentLocked: true, assignedByUserId: 'boss' }, TeamRole.Admin);

    expect(screen.getByTestId('task-abandon-button')).toBeTruthy();
    expect(screen.queryByTestId('task-locked-note')).toBeNull();
  });

  it("warns that the deadline falls in the user's time off", async () => {
    (getMyAbsences as jest.Mock).mockResolvedValue([{ itemId: 'a', startUtc: inDays(2), endUtc: inDays(6) }]);

    await show(base);

    await waitFor(() => expect(screen.getByTestId('absence-deadline-note')).toBeTruthy());
  });
});
