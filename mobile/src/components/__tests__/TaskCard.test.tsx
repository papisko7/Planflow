import { fireEvent, render, screen } from '@testing-library/react-native';
import * as authService from '../../services/authService';
import { TaskCard } from '../TaskCard';
import { TaskStatus, type TaskDto } from '../../types/task';
import { PLAIN_TASK_FIELDS } from '../../test-fixtures';

const task: TaskDto = {
  id: 't1',
  teamId: 'team1',
  assignedUserId: null,
  title: 'Write report',
  description: null,
  status: TaskStatus.Todo,
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

describe('TaskCard long-press menu', () => {
  const actions = { edit: jest.fn(), remove: jest.fn(), complete: jest.fn(), reopen: jest.fn(), claim: jest.fn(), abandon: jest.fn() };

  beforeEach(() => jest.clearAllMocks());

  it('opens the quick-action menu and runs the chosen action', async () => {
    await render(<TaskCard task={task} actions={actions} />);

    await fireEvent(screen.getByTestId('task-card-t1'), 'longPress');
    await fireEvent.press(screen.getByTestId('task-menu-complete'));

    expect(actions.complete).toHaveBeenCalledWith(task);
  });

  it('offers "Take over" only for an unassigned task', async () => {
    await render(<TaskCard task={task} actions={actions} />);
    await fireEvent(screen.getByTestId('task-card-t1'), 'longPress');
    await fireEvent.press(screen.getByTestId('task-menu-claim'));
    expect(actions.claim).toHaveBeenCalledWith(task);
  });

  it('hides "Take over" once the task has an assignee', async () => {
    await render(<TaskCard task={{ ...task, assignedUserId: 'u1' }} actions={actions} />);
    await fireEvent(screen.getByTestId('task-card-t1'), 'longPress');
    expect(screen.queryByTestId('task-menu-claim')).toBeNull();
  });

  it('offers "Abandon" on a task assigned to the current user', async () => {
    jest.spyOn(authService, 'getCurrentUserId').mockReturnValue('me');
    const mine = { ...task, assignedUserId: 'me' };
    await render(<TaskCard task={mine} actions={actions} />);

    await fireEvent(screen.getByTestId('task-card-t1'), 'longPress');
    await fireEvent.press(screen.getByTestId('task-menu-abandon'));

    expect(actions.abandon).toHaveBeenCalledWith(mine);
  });

  it('hides "Abandon" on someone else\'s task', async () => {
    jest.spyOn(authService, 'getCurrentUserId').mockReturnValue('me');
    await render(<TaskCard task={{ ...task, assignedUserId: 'someone-else' }} actions={actions} />);

    await fireEvent(screen.getByTestId('task-card-t1'), 'longPress');

    expect(screen.queryByTestId('task-menu-abandon')).toBeNull();
  });

  it('offers no menu when the card has no actions', async () => {
    await render(<TaskCard task={task} />);

    await fireEvent(screen.getByTestId('task-card-t1'), 'longPress');

    expect(screen.queryByTestId('task-menu-delete')).toBeNull();
  });

  it('marks an open task as done on double tap, without opening it', async () => {
    jest.useFakeTimers();
    const onPress = jest.fn();
    await render(<TaskCard task={task} onPress={onPress} actions={actions} />);

    await fireEvent.press(screen.getByTestId('task-card-t1'));
    await fireEvent.press(screen.getByTestId('task-card-t1'));
    jest.advanceTimersByTime(500);

    expect(actions.complete).toHaveBeenCalledWith(task);
    expect(onPress).not.toHaveBeenCalled();
    jest.useRealTimers();
  });

  it('opens the task on a single tap after the double-tap window', async () => {
    jest.useFakeTimers();
    const onPress = jest.fn();
    await render(<TaskCard task={task} onPress={onPress} actions={actions} />);

    await fireEvent.press(screen.getByTestId('task-card-t1'));
    expect(onPress).not.toHaveBeenCalled();
    jest.advanceTimersByTime(300);

    expect(onPress).toHaveBeenCalledWith(task);
    expect(actions.complete).not.toHaveBeenCalled();
    jest.useRealTimers();
  });

  it('reopens a finished task on double tap, without opening it', async () => {
    jest.useFakeTimers();
    const onPress = jest.fn();
    const done = { ...task, status: TaskStatus.Done };
    await render(<TaskCard task={done} onPress={onPress} actions={actions} />);

    await fireEvent.press(screen.getByTestId('task-card-t1'));
    await fireEvent.press(screen.getByTestId('task-card-t1'));
    jest.advanceTimersByTime(500);

    expect(actions.reopen).toHaveBeenCalledWith(done);
    expect(actions.complete).not.toHaveBeenCalled();
    expect(onPress).not.toHaveBeenCalled();
    jest.useRealTimers();
  });

  it('offers "Mark as not done" instead of "Mark as done" for a finished task', async () => {
    const done = { ...task, status: TaskStatus.Done };
    await render(<TaskCard task={done} actions={actions} />);

    await fireEvent(screen.getByTestId('task-card-t1'), 'longPress');

    expect(screen.queryByTestId('task-menu-complete')).toBeNull();
    expect(screen.getByTestId('task-menu-delete')).toBeTruthy();

    await fireEvent.press(screen.getByTestId('task-menu-reopen'));
    expect(actions.reopen).toHaveBeenCalledWith(done);
  });
});
