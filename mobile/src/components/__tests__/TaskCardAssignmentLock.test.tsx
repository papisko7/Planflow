import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { TaskCard } from '../TaskCard';
import { getMyAbsences } from '../../services/userService';
import { resetMyAbsencesCache } from '../../hooks/useMyAbsences';
import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { TaskStatus, type TaskDto } from '../../types/task';

jest.mock('../../services/authService', () => ({ getCurrentUserId: () => 'me' }));
jest.mock('../../services/userService');

const task: TaskDto = {
  id: 't1',
  teamId: 'team1',
  assignedUserId: 'me',
  title: 'Write report',
  description: null,
  status: TaskStatus.Backlog,
  deadlineUtc: null,
  impactScore: 1,
  blockedByTaskId: null,
  manualUrgencyOverride: null,
  manualUrgencyOverrideExpiresAtUtc: null,
  currentUrgencyScore: 0.5,
  createdAtUtc: '2026-01-01T00:00:00Z',
  updatedAtUtc: null,
  ...PLAIN_TASK_FIELDS,
};

const actions = { edit: jest.fn(), remove: jest.fn(), complete: jest.fn(), reopen: jest.fn(), claim: jest.fn(), abandon: jest.fn() };

const inDays = (days: number) => new Date(Date.now() + days * 86_400_000).toISOString();

beforeEach(() => {
  jest.clearAllMocks();
  resetMyAbsencesCache();
  (getMyAbsences as jest.Mock).mockResolvedValue([]);
});

// A task an Owner/Admin assigned cannot be given back, and a deadline inside the person's time off is flagged.
describe('TaskCard - assignment lock', () => {
  it('offers "Abandon" on an own task that is not locked', async () => {
    await render(<TaskCard task={task} actions={actions} />);

    await fireEvent(screen.getByTestId('task-card-t1'), 'longPress');

    expect(screen.getByTestId('task-menu-abandon')).toBeTruthy();
  });

  it('hides "Abandon" on a task an Owner/Admin assigned', async () => {
    await render(<TaskCard task={{ ...task, isAssignmentLocked: true, assignedByUserId: 'boss' }} actions={actions} />);

    await fireEvent(screen.getByTestId('task-card-t1'), 'longPress');

    expect(screen.queryByTestId('task-menu-abandon')).toBeNull();
    expect(screen.getByTestId('task-menu-edit')).toBeTruthy(); // working on it stays possible
  });
});

describe('TaskCard - deadline in time off', () => {
  const due = { ...task, deadlineUtc: inDays(3) };

  it("warns when the deadline falls inside the signed-in user's absence", async () => {
    (getMyAbsences as jest.Mock).mockResolvedValue([{ itemId: 'a', startUtc: inDays(2), endUtc: inDays(5) }]);

    await render(<TaskCard task={due} />);

    await waitFor(() => expect(screen.getByTestId('absence-note-t1')).toBeTruthy());
  });

  it('says nothing when there is no absence at the deadline', async () => {
    (getMyAbsences as jest.Mock).mockResolvedValue([{ itemId: 'a', startUtc: inDays(5), endUtc: inDays(8) }]);

    await render(<TaskCard task={due} />);

    await waitFor(() => expect(getMyAbsences).toHaveBeenCalled());
    expect(screen.queryByTestId('absence-note-t1')).toBeNull();
  });

  it('does not even ask for the absences on a task of somebody else', async () => {
    await render(<TaskCard task={{ ...due, assignedUserId: 'ann' }} />);

    expect(getMyAbsences).not.toHaveBeenCalled();
    expect(screen.queryByTestId('absence-note-t1')).toBeNull();
  });

  it('stays quiet when the absences cannot be loaded', async () => {
    (getMyAbsences as jest.Mock).mockRejectedValue(new Error('offline'));

    await render(<TaskCard task={due} />);

    await waitFor(() => expect(getMyAbsences).toHaveBeenCalled());
    expect(screen.queryByTestId('absence-note-t1')).toBeNull();
  });
});
