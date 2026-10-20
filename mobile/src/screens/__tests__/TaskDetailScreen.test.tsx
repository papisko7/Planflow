import { render, screen, waitFor } from '@testing-library/react-native';
import TaskDetailScreen from '../TaskDetailScreen';
import { getTask, getTeamTasks } from '../../services/taskService';
import { getTeamMembers } from '../../services/teamService';
import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { EventTemplate, TaskStatus, TaskType, type TaskDetailDto, type TaskDto } from '../../types/task';

jest.mock('../../services/taskService');
jest.mock('../../services/teamService');
jest.mock('../../services/authService', () => ({ getCurrentUserId: () => 'me' }));
jest.mock('@react-navigation/native', () => ({
  useNavigation: () => ({ goBack: jest.fn() }),
  useRoute: () => ({ params: { taskId: 'e1', teamId: 'team' } }),
}));

const importedEvent = (over: Partial<TaskDto> = {}): TaskDto => ({
  id: 'e1',
  teamId: 'team',
  assignedUserId: 'me',
  title: 'Conference',
  description: null,
  status: TaskStatus.Confirmed,
  deadlineUtc: null,
  impactScore: null,
  blockedByTaskId: null,
  manualUrgencyOverride: null,
  manualUrgencyOverrideExpiresAtUtc: null,
  currentUrgencyScore: 0.4,
  createdAtUtc: '2026-10-01T00:00:00Z',
  updatedAtUtc: null,
  ...PLAIN_TASK_FIELDS,
  taskType: TaskType.Event,
  startUtc: '2026-10-20T00:00:00Z',
  endUtc: '2026-10-22T00:00:00Z',
  isAllDay: true,
  ...over,
});

const showDetail = async (detail: TaskDetailDto) => {
  (getTask as jest.Mock).mockResolvedValue(detail);
  (getTeamTasks as jest.Mock).mockResolvedValue([]);
  (getTeamMembers as jest.Mock).mockResolvedValue([]);
  await render(<TaskDetailScreen />);
  await waitFor(() => expect(screen.getByText('Conference')).toBeTruthy());
};

describe('TaskDetailScreen - imported Google event', () => {
  beforeEach(() => jest.clearAllMocks());

  it('shows an all-day event as its first and LAST day, not the midnight after it', async () => {
    await showDetail({ task: importedEvent(), latestScoreBreakdown: null });

    // Stored: 20 Oct 00:00Z to 22 Oct 00:00Z (the end is the midnight after the last day). Shown: Starts 20th, Ends 21st.
    const day = (d: number) => new Date(2026, 9, d).toLocaleDateString(undefined, { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' });
    expect(screen.getByText('All day')).toBeTruthy();
    expect(screen.getByText(day(20))).toBeTruthy();
    expect(screen.getByText(day(21))).toBeTruthy();
    expect(screen.queryByText(day(22))).toBeNull();
  });

  it('shows location, Busy/Free, colour, Google settings and the template badge', async () => {
    await showDetail({
      task: importedEvent({
        eventTemplate: EventTemplate.Default,
        location: 'Room 4, Main St',
        colorKey: 'cherry-blossom',
        isBusy: false,
        externalSettings: 'Do not disturb',
      }),
      latestScoreBreakdown: null,
    });

    expect(screen.getByText('Room 4, Main St')).toBeTruthy();
    expect(screen.getByText('Free')).toBeTruthy();
    expect(screen.getByText('Cherry blossom')).toBeTruthy();
    expect(screen.getByTestId('task-color-swatch')).toBeTruthy();
    expect(screen.getByText('Do not disturb')).toBeTruthy();
    expect(screen.queryByTestId('task-template-badge')).toBeNull(); // an ordinary event has no template badge
  });

  it('labels an Out of office event and does not offer Busy/Free for it', async () => {
    await showDetail({ task: importedEvent({ eventTemplate: EventTemplate.OutOfOffice }), latestScoreBreakdown: null });

    expect(screen.getByTestId('task-template-badge').props.children).toBe('Out of office');
    expect(screen.queryByText('Show as')).toBeNull();
  });

  it('lists the reminders imported from Google in plain words', async () => {
    await showDetail({
      task: importedEvent(),
      latestScoreBreakdown: null,
      reminders: [
        { offsetMinutes: null, daysBefore: 1, timeOfDayMinutes: 540 },
        { offsetMinutes: 30, daysBefore: null, timeOfDayMinutes: null },
      ],
    });

    expect(screen.getByText('Reminders')).toBeTruthy();
    expect(screen.getByText(/1 day before at 09:00/)).toBeTruthy();
    expect(screen.getByText(/30 minutes before/)).toBeTruthy();
  });

  it('shows no Reminders row when there are none (or the server does not send them)', async () => {
    await showDetail({ task: importedEvent(), latestScoreBreakdown: null });

    expect(screen.queryByText('Reminders')).toBeNull();
  });
});

// Informational items have no score; a task shows its planned start and the Scheduled row of the breakdown.
describe('TaskDetailScreen - scoring', () => {
  beforeEach(() => jest.clearAllMocks());

  const breakdown = (over: Record<string, unknown> = {}) => ({
    deadlineComponent: 0.5, blockingComponent: 0, impactComponent: 0.5, userOverrideComponent: 0, finalScore: 0.42,
    triggerSource: 0, createdAtUtc: '2026-10-18T10:00:00Z', isBlocked: false, reasonText: '[Medium] due in 2d, impact 5/10', ...over,
  });

  it('an Out of office has neither an impact score nor a blocking or score section', async () => {
    await showDetail({ task: importedEvent({ eventTemplate: EventTemplate.OutOfOffice }), latestScoreBreakdown: null });

    expect(screen.queryByText('Impact score')).toBeNull();
    expect(screen.queryByText('Urgency Score Breakdown')).toBeNull();
    expect(screen.queryByText('Blocking Relationships')).toBeNull();
  });

  it('a plain event keeps them', async () => {
    await showDetail({ task: importedEvent(), latestScoreBreakdown: null });

    expect(screen.getByText('Impact score')).toBeTruthy();
    expect(screen.getByText('Urgency Score Breakdown')).toBeTruthy();
  });

  it('a task with a planned start shows it, and the breakdown has the Planned start row', async () => {
    await showDetail({
      task: importedEvent({ taskType: TaskType.Task, eventTemplate: EventTemplate.Default, isAllDay: false, startUtc: '2030-10-20T09:00:00Z', endUtc: null, deadlineUtc: '2030-10-25T09:00:00Z', status: TaskStatus.Backlog }),
      latestScoreBreakdown: breakdown({ scheduledComponent: 0.87, algorithmVersion: 2 }),
    });

    expect(screen.getByText('Planned start')).toBeTruthy();
    expect(screen.getByText('0.87')).toBeTruthy();
  });

  it('the breakdown has no Planned start row when it did not count (and none on a score from the older algorithm)', async () => {
    await showDetail({ task: importedEvent({ taskType: TaskType.Task, eventTemplate: EventTemplate.Default, isAllDay: false, startUtc: null, endUtc: null, deadlineUtc: '2030-10-25T09:00:00Z', status: TaskStatus.Backlog }), latestScoreBreakdown: breakdown({ scheduledComponent: null }) });

    expect(screen.getByText(/Deadline/)).toBeTruthy(); // the other rows are there
    expect(screen.queryByText('Planned start')).toBeNull();
  });
});

// A task imported from Google Tasks is due on a DAY (stored as midnight UTC), so it is shown as a date, never as a clock time.
describe('TaskDetailScreen - task from Google Tasks', () => {
  beforeEach(() => jest.clearAllMocks());

  const googleTask = (over: Partial<TaskDto> = {}): TaskDto =>
    importedEvent({
      taskType: TaskType.Task,
      status: TaskStatus.Backlog,
      isExternal: true,
      isAllDay: true,
      startUtc: '2026-10-18T00:00:00Z',
      endUtc: null,
      deadlineUtc: '2026-10-25T00:00:00Z',
      googleTaskListTitle: 'My Tasks',
      ...over,
    });
  const day = (d: number) => new Date(2026, 9, d).toLocaleDateString(undefined, { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' });

  it('shows the due day and the planned start as dates, and the Google list it came from', async () => {
    await showDetail({ task: googleTask(), latestScoreBreakdown: null });

    expect(screen.getByText(day(25))).toBeTruthy();
    expect(screen.getByText(day(18))).toBeTruthy();
    expect(screen.getByText('Google Tasks list')).toBeTruthy();
    expect(screen.getByText('My Tasks')).toBeTruthy();
  });

  it('says when the deadline is only a placeholder because Google had no due day', async () => {
    await showDetail({ task: googleTask({ isDeadlineDefaulted: true }), latestScoreBreakdown: null });

    expect(screen.getByText(`${day(25)} (not set in Google)`)).toBeTruthy();
  });

  it('says that an edit has not reached Google yet', async () => {
    await showDetail({ task: googleTask({ hasUnsentChanges: true }), latestScoreBreakdown: null });

    expect(screen.getByText('Your changes are sent at the next sync')).toBeTruthy();
  });

  it('says nothing about sending once the edit has reached Google', async () => {
    await showDetail({ task: googleTask({ hasUnsentChanges: false }), latestScoreBreakdown: null });

    expect(screen.queryByText('Your changes are sent at the next sync')).toBeNull();
  });
});
