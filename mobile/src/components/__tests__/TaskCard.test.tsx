import { fireEvent, render, screen } from '@testing-library/react-native';
import * as authService from '../../services/authService';
import { TaskCard } from '../TaskCard';
import { EventTemplate, TaskStatus, TaskType, type TaskDto } from '../../types/task';
import { PLAIN_TASK_FIELDS } from '../../test-fixtures';

const task: TaskDto = {
  id: 't1',
  teamId: 'team1',
  assignedUserId: null,
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

  // Step 7.5.11: the double tap no longer completes a task - it ticks the card and starts the selection mode.
  it('ticks the card on a double tap (starting the selection mode), without opening it or completing it', async () => {
    jest.useFakeTimers();
    const onPress = jest.fn();
    const onToggleSelect = jest.fn();
    await render(<TaskCard task={task} onPress={onPress} actions={actions} onToggleSelect={onToggleSelect} />);

    await fireEvent.press(screen.getByTestId('task-card-t1'));
    await fireEvent.press(screen.getByTestId('task-card-t1'));
    jest.advanceTimersByTime(500);

    expect(onToggleSelect).toHaveBeenCalledTimes(1);
    expect(onToggleSelect).toHaveBeenCalledWith(task);
    expect(onPress).not.toHaveBeenCalled();
    expect(actions.complete).not.toHaveBeenCalled();
    expect(actions.reopen).not.toHaveBeenCalled();
    jest.useRealTimers();
  });

  it('opens the task on a single tap after the double-tap window', async () => {
    jest.useFakeTimers();
    const onPress = jest.fn();
    const onToggleSelect = jest.fn();
    await render(<TaskCard task={task} onPress={onPress} actions={actions} onToggleSelect={onToggleSelect} />);

    await fireEvent.press(screen.getByTestId('task-card-t1'));
    expect(onPress).not.toHaveBeenCalled();
    jest.advanceTimersByTime(300);

    expect(onPress).toHaveBeenCalledWith(task);
    expect(onToggleSelect).not.toHaveBeenCalled();
    jest.useRealTimers();
  });

  it('an event is ticked by a double tap and opened by a single tap the same way a task is', async () => {
    jest.useFakeTimers();
    const onPress = jest.fn();
    const onToggleSelect = jest.fn();
    const event = { ...task, taskType: TaskType.Event, status: TaskStatus.Confirmed };
    await render(<TaskCard task={event} onPress={onPress} actions={actions} onToggleSelect={onToggleSelect} />);

    await fireEvent.press(screen.getByTestId('task-card-t1'));
    await fireEvent.press(screen.getByTestId('task-card-t1'));
    jest.advanceTimersByTime(500);
    expect(onToggleSelect).toHaveBeenCalledWith(event);

    await fireEvent.press(screen.getByTestId('task-card-t1'));
    jest.advanceTimersByTime(500);
    expect(onPress).toHaveBeenCalledWith(event);
    jest.useRealTimers();
  });

  it('while selecting, a tap ticks or unticks the card (it does not open) and a long press ticks too, with no menu', async () => {
    const onPress = jest.fn();
    const onToggleSelect = jest.fn();
    await render(<TaskCard task={task} onPress={onPress} actions={actions} onToggleSelect={onToggleSelect} selectionMode selected />);

    expect(screen.getByTestId('task-ticked-t1')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('task-card-t1'));
    await fireEvent(screen.getByTestId('task-card-t1'), 'longPress');

    expect(onToggleSelect).toHaveBeenCalledTimes(2);
    expect(onPress).not.toHaveBeenCalled();
    expect(screen.queryByTestId('task-menu-delete')).toBeNull();
  });

  it('shows an empty box on a card that is not ticked while the mode is on, and no box otherwise', async () => {
    await render(<TaskCard task={task} onToggleSelect={jest.fn()} selectionMode />);
    expect(screen.getByTestId('task-unticked-t1')).toBeTruthy();

    await render(<TaskCard task={{ ...task, id: 't2' }} onToggleSelect={jest.fn()} />);
    expect(screen.queryByTestId('task-unticked-t2')).toBeNull();
    expect(screen.queryByTestId('task-ticked-t2')).toBeNull();
  });

  it('offers "Select" in the long-press menu, which ticks the card', async () => {
    const onToggleSelect = jest.fn();
    await render(<TaskCard task={task} actions={actions} onToggleSelect={onToggleSelect} />);

    await fireEvent(screen.getByTestId('task-card-t1'), 'longPress');
    await fireEvent.press(screen.getByTestId('task-menu-select'));

    expect(onToggleSelect).toHaveBeenCalledWith(task);
  });

  it('keeps done / not done in the long-press menu, where the double tap used to be', async () => {
    await render(<TaskCard task={task} actions={actions} onToggleSelect={jest.fn()} />);

    await fireEvent(screen.getByTestId('task-card-t1'), 'longPress');
    await fireEvent.press(screen.getByTestId('task-menu-complete'));

    expect(actions.complete).toHaveBeenCalledWith(task);
  });

  it('a series entry says how many other occurrences it stands for', async () => {
    await render(<TaskCard task={{ ...task, seriesMore: 12 }} />);

    expect(screen.getByText('🔁 +12 more')).toBeTruthy();
  });

  it('offers "Mark as not done" instead of "Mark as done" for a finished task', async () => {
    const done = { ...task, status: TaskStatus.Completed };
    await render(<TaskCard task={done} actions={actions} />);

    await fireEvent(screen.getByTestId('task-card-t1'), 'longPress');

    expect(screen.queryByTestId('task-menu-complete')).toBeNull();
    expect(screen.getByTestId('task-menu-delete')).toBeTruthy();

    await fireEvent.press(screen.getByTestId('task-menu-reopen'));
    expect(actions.reopen).toHaveBeenCalledWith(done);
  });

  it('does not offer "Mark as done" for an event - an event is Confirmed / Tentative / Cancelled, never done', async () => {
    const event = { ...task, taskType: TaskType.Event, status: TaskStatus.Confirmed };
    await render(<TaskCard task={event} actions={actions} />);

    await fireEvent(screen.getByTestId('task-card-t1'), 'longPress');

    expect(screen.queryByTestId('task-menu-complete')).toBeNull();
    expect(screen.queryByTestId('task-menu-reopen')).toBeNull();
    expect(screen.getByTestId('task-menu-delete')).toBeTruthy();
  });

  it('opens a card at once on a single tap when its list has no selection - there is no double tap to wait for', async () => {
    const onPress = jest.fn();
    const event = { ...task, taskType: TaskType.Event, status: TaskStatus.Confirmed };
    await render(<TaskCard task={event} onPress={onPress} actions={actions} />);

    await fireEvent.press(screen.getByTestId('task-card-t1'));

    expect(onPress).toHaveBeenCalledWith(event);
    expect(actions.complete).not.toHaveBeenCalled();
  });

  it('flags an item that still needs details (imported without impact), and only then', async () => {
    await render(<TaskCard task={{ ...task, isDataIncomplete: true }} />);
    expect(screen.getByTestId('needs-details-t1')).toBeTruthy();

    await render(<TaskCard task={{ ...task, id: 't2', isDataIncomplete: false }} />);
    expect(screen.queryByTestId('needs-details-t2')).toBeNull();
  });

  it('shows the place and the kind of an imported event (Out of office, Focus time...), and nothing for an ordinary one', async () => {
    const holiday: TaskDto = {
      ...task,
      id: 'e1',
      taskType: TaskType.Event,
      eventTemplate: EventTemplate.OutOfOffice,
      location: 'Lisbon',
      isAllDay: true,
      startUtc: '2026-10-20T00:00:00Z',
      endUtc: '2026-10-21T00:00:00Z',
    };
    await render(<TaskCard task={holiday} />);
    expect(screen.getByTestId('item-template-e1').props.children).toBe('Out of office');
    expect(screen.getByText(/Lisbon/)).toBeTruthy();
    expect(screen.getByText(/all day/)).toBeTruthy();

    await render(<TaskCard task={{ ...task, id: 'plain' }} />);
    expect(screen.queryByTestId('item-template-plain')).toBeNull();
  });

  it('shows a dot in the item colour, and none for the default colour', async () => {
    await render(<TaskCard task={{ ...task, id: 'c1', colorKey: 'tomato' }} />);
    expect(screen.getByTestId('item-color-c1').props.style).toEqual(expect.arrayContaining([expect.objectContaining({ backgroundColor: '#d50004' })]));

    await render(<TaskCard task={{ ...task, id: 'c2', colorKey: null }} />);
    expect(screen.queryByTestId('item-color-c2')).toBeNull();
  });
});

// Step 7.5.4: an informational item (Out of office, ...) is shown in the calendar but is not ranked.
describe('TaskCard - informational items', () => {
  const leave: TaskDto = {
    ...task, id: 'leave', title: 'Holiday', taskType: TaskType.Event, eventTemplate: EventTemplate.OutOfOffice, status: TaskStatus.Confirmed,
    currentUrgencyScore: 0, startUtc: '2030-10-20T00:00:00Z', endUtc: '2030-10-22T00:00:00Z', isAllDay: true,
    reasonText: '[Critical] a stale reason from the old algorithm',
  };

  it('shows no score pill and no urgency explanation, but still the template and the title', async () => {
    await render(<TaskCard task={leave} />);

    expect(screen.getByText('Holiday')).toBeTruthy();
    expect(screen.getByTestId('item-template-leave')).toBeTruthy();
    expect(screen.queryByTestId('score-pill-leave')).toBeNull();
    expect(screen.queryByText(/stale reason/)).toBeNull();
    expect(screen.queryByText(/Low|Critical/)).toBeNull();
  });

  it('a task still shows its score pill', async () => {
    await render(<TaskCard task={task} />);

    expect(screen.getByTestId('score-pill-t1')).toBeTruthy();
    expect(screen.getByText('0.50')).toBeTruthy();
  });
});
