import { Alert, type AlertButton } from 'react-native';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import ItemsScreen from '../ItemsScreen';
import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { deleteList } from '../../services/listService';
import { bulkDeleteTasks, bulkMoveToList, bulkSetStatus } from '../../services/taskService';
import { ItemListKind, type ItemListDto } from '../../types/itemList';
import { TaskStatus, TaskType, type TaskWithTeam } from '../../types/task';

jest.mock('../../services/taskService');
jest.mock('../../services/listService');

const DAY = 86_400_000;
const inDays = (d: number) => new Date(Date.now() + d * DAY).toISOString();

const mockItem = (over: Partial<TaskWithTeam>): TaskWithTeam => ({
  id: 'x', teamId: 't', teamName: 'Team', assignedUserId: null, title: 'Item', description: null, status: TaskStatus.Backlog,
  deadlineUtc: null, impactScore: null, blockedByTaskId: null, manualUrgencyOverride: null, manualUrgencyOverrideExpiresAtUtc: null,
  currentUrgencyScore: 0, createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: null, ...PLAIN_TASK_FIELDS, ...over,
} as TaskWithTeam);

const event = (id: string, title: string, days: number, over: Partial<TaskWithTeam> = {}) =>
  mockItem({ id, title, taskType: TaskType.Event, status: TaskStatus.Confirmed, startUtc: inDays(days), endUtc: new Date(Date.now() + days * DAY + 3_600_000).toISOString(), ...over });

const sprint: ItemListDto = { id: 'l-sprint', teamId: 't', name: 'Sprint', kind: ItemListKind.TaskList, createdByUserId: 'u' };
const standups: ItemListDto = { id: 'l-standups', teamId: 't', name: 'Standups', kind: ItemListKind.MeetingList, createdByUserId: 'u' };

let mockItems: TaskWithTeam[] = [];
let mockLists: ItemListDto[] = [];
const mockReload = jest.fn();
const mockNavigate = jest.fn();

jest.mock('../../hooks/useAllTasks', () => ({
  useAllTasks: () => ({
    state: 'ready', tasks: mockItems, lists: mockLists, teams: [{ id: 't', name: 'Team', isPersonal: false }], errorMessage: null, refreshing: false,
    refresh: jest.fn(), reload: mockReload,
  }),
}));
jest.mock('../../hooks/useTaskActions', () => ({ useTaskActions: () => ({}) }));
jest.mock('@react-navigation/native', () => ({ useNavigation: () => ({ navigate: mockNavigate }), useScrollToTop: jest.fn() }));

const mockedDelete = bulkDeleteTasks as jest.MockedFunction<typeof bulkDeleteTasks>;
const mockedStatus = bulkSetStatus as jest.MockedFunction<typeof bulkSetStatus>;
const mockedMove = bulkMoveToList as jest.MockedFunction<typeof bulkMoveToList>;
const mockedDeleteList = deleteList as jest.MockedFunction<typeof deleteList>;

let alertSpy: jest.SpyInstance;
const pressAlertButton = async (text: string) => {
  const buttons = alertSpy.mock.calls[alertSpy.mock.calls.length - 1][2] as AlertButton[];
  await act(async () => {
    await buttons.find((b) => b.text === text)?.onPress?.();
  });
};

// A double tap = two presses straight after each other.
const doubleTap = async (testID: string) => {
  await fireEvent.press(screen.getByTestId(testID));
  await fireEvent.press(screen.getByTestId(testID));
};

// Selection mode, the bar, "Clear" and the team's own lists on the Items screen.
describe('ItemsScreen - selection mode', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    alertSpy = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);
    mockedDelete.mockResolvedValue({ succeeded: 0, skippedIds: [] });
    mockedStatus.mockResolvedValue({ succeeded: 0, skippedIds: [] });
    mockedMove.mockResolvedValue({ succeeded: 0, skippedIds: [] });
    mockItems = [
      mockItem({ id: 'a', title: 'Write report' }),
      mockItem({ id: 'b', title: 'Pay invoice' }),
      event('e', 'Dentist', 2),
    ];
    mockLists = [];
  });

  afterEach(() => alertSpy.mockRestore());

  it('a double tap ticks the card and shows the bar; a single tap still opens the item (after a short wait)', async () => {
    await render(<ItemsScreen />);
    expect(screen.queryByTestId('selection-bar')).toBeNull();
    expect(screen.getByTestId('fab-new-task')).toBeTruthy();

    await fireEvent.press(screen.getByTestId('task-card-b'));
    await waitFor(() => expect(mockNavigate).toHaveBeenCalledWith('TaskDetail', { taskId: 'b', teamId: 't' }));
    expect(screen.queryByTestId('selection-bar')).toBeNull();

    await doubleTap('task-card-a');

    expect(screen.getByTestId('selection-bar')).toBeTruthy();
    expect(screen.getByTestId('selection-count')).toHaveProp('children', [1, ' selected']);
    expect(screen.getByTestId('task-ticked-a')).toBeTruthy();
    expect(screen.getByTestId('task-unticked-b')).toBeTruthy();
    expect(screen.queryByTestId('fab-new-task')).toBeNull(); // the + button gives way to the bar
  });

  it('taps tick more cards, "Select all" ticks the open tab, untick the last one or press the cross to leave', async () => {
    await render(<ItemsScreen />);
    await doubleTap('task-card-a');

    await fireEvent.press(screen.getByTestId('task-card-b'));
    expect(screen.getByTestId('task-ticked-b')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('selection-select-all'));
    expect(screen.getByTestId('task-ticked-e')).toBeTruthy();

    await fireEvent.press(screen.getByTestId('selection-cancel'));
    expect(screen.queryByTestId('selection-bar')).toBeNull();

    await doubleTap('task-card-a');
    await fireEvent.press(screen.getByTestId('task-card-a'));
    expect(screen.queryByTestId('selection-bar')).toBeNull();
  });

  it('"Delete" asks first, then deletes exactly the ticked items and leaves the mode', async () => {
    mockedDelete.mockResolvedValue({ succeeded: 2, skippedIds: [] });
    await render(<ItemsScreen />);
    await doubleTap('task-card-a');
    await fireEvent.press(screen.getByTestId('task-card-b'));

    await fireEvent.press(screen.getByTestId('selection-delete'));
    expect(alertSpy.mock.calls[0][0]).toBe('Delete 2 items?');
    expect(mockedDelete).not.toHaveBeenCalled();
    await pressAlertButton('Delete');

    expect(mockedDelete).toHaveBeenCalledWith('t', ['a', 'b']);
    expect(mockReload).toHaveBeenCalled();
    expect(screen.queryByTestId('selection-bar')).toBeNull();
  });

  it('tells the person when some items were left alone', async () => {
    mockedDelete.mockResolvedValue({ succeeded: 1, skippedIds: ['b'] });
    await render(<ItemsScreen />);
    await doubleTap('task-card-a');
    await fireEvent.press(screen.getByTestId('task-card-b'));
    await fireEvent.press(screen.getByTestId('selection-delete'));

    await pressAlertButton('Delete');

    expect(alertSpy.mock.calls[alertSpy.mock.calls.length - 1][0]).toBe('1 item left unchanged');
  });

  it('"Status" offers the statuses every ticked item has: four for tasks only, just Cancelled for a mix', async () => {
    await render(<ItemsScreen />);
    await doubleTap('task-card-a');
    await fireEvent.press(screen.getByTestId('task-card-b'));
    await fireEvent.press(screen.getByTestId('selection-status'));

    expect(screen.getByTestId(`status-option-${TaskStatus.Completed}`)).toBeTruthy();
    expect(screen.getByTestId(`status-option-${TaskStatus.InProgress}`)).toBeTruthy();
    expect(screen.queryByTestId(`status-option-${TaskStatus.Confirmed}`)).toBeNull();

    await fireEvent.press(screen.getByTestId('option-sheet-cancel'));
    await fireEvent.press(screen.getByTestId('task-card-e')); // add the event: now it is a mix
    await fireEvent.press(screen.getByTestId('selection-status'));

    expect(screen.getByTestId(`status-option-${TaskStatus.Cancelled}`)).toBeTruthy();
    expect(screen.queryByTestId(`status-option-${TaskStatus.Completed}`)).toBeNull();
  });

  it('picking a status sets it on the ticked items with one call per team', async () => {
    mockedStatus.mockResolvedValue({ succeeded: 2, skippedIds: [] });
    await render(<ItemsScreen />);
    await doubleTap('task-card-a');
    await fireEvent.press(screen.getByTestId('task-card-b'));
    await fireEvent.press(screen.getByTestId('selection-status'));

    await fireEvent.press(screen.getByTestId(`status-option-${TaskStatus.Completed}`));

    await waitFor(() => expect(mockedStatus).toHaveBeenCalledWith('t', ['a', 'b'], TaskStatus.Completed));
    expect(screen.queryByTestId('selection-bar')).toBeNull();
  });

  it('"Move to list" offers the task lists for tasks (with "No list"), and moves them', async () => {
    mockLists = [sprint, standups];
    mockedMove.mockResolvedValue({ succeeded: 2, skippedIds: [] });
    await render(<ItemsScreen />);
    await doubleTap('task-card-a');
    await fireEvent.press(screen.getByTestId('task-card-b'));
    await fireEvent.press(screen.getByTestId('selection-move'));

    expect(screen.getByTestId('move-option-l-sprint')).toBeTruthy();
    expect(screen.queryByTestId('move-option-l-standups')).toBeNull(); // a meeting list does not take tasks
    expect(screen.getByTestId('move-option-none')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('move-option-l-sprint'));

    await waitFor(() => expect(mockedMove).toHaveBeenCalledWith('t', ['a', 'b'], 'l-sprint'));
  });

  it('"Move to list" refuses a mix of tasks and events', async () => {
    mockLists = [sprint];
    await render(<ItemsScreen />);
    await doubleTap('task-card-a');
    await fireEvent.press(screen.getByTestId('task-card-e'));

    await fireEvent.press(screen.getByTestId('selection-move'));

    expect(alertSpy.mock.calls[0][0]).toBe('Pick one kind of item');
    expect(screen.queryByTestId('move-option-l-sprint')).toBeNull();
  });

  it('ticking the entry of a repeating series acts for every occurrence of it', async () => {
    mockItems = [
      event('w1', 'Weekly sync', 2, { seriesId: 'weekly' }),
      event('w2', 'Weekly sync', 9, { seriesId: 'weekly' }),
      event('w3', 'Weekly sync', 16, { seriesId: 'weekly' }),
      mockItem({ id: 'a', title: 'Write report' }),
    ];
    mockedDelete.mockResolvedValue({ succeeded: 3, skippedIds: [] });
    await render(<ItemsScreen />);

    expect(screen.queryByTestId('task-card-w2')).toBeNull(); // one entry for the series ...
    expect(screen.getByText('🔁 +2 more')).toBeTruthy(); // ... saying how many it stands for
    await doubleTap('task-card-w1');
    await fireEvent.press(screen.getByTestId('selection-delete'));
    expect(alertSpy.mock.calls[0][0]).toBe('Delete 3 items?');
    expect(alertSpy.mock.calls[0][1]).toBe('Includes 2 more occurrences of repeating events.');
    await pressAlertButton('Delete');

    expect(mockedDelete).toHaveBeenCalledWith('t', ['w1', 'w2', 'w3']);
  });
});

describe('ItemsScreen - Clear', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    alertSpy = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);
    mockedDelete.mockResolvedValue({ succeeded: 0, skippedIds: [] });
    mockLists = [];
    mockItems = [
      mockItem({ id: 'a', title: 'Write report' }),
      mockItem({ id: 'g', title: 'Buy milk', isExternal: true, isAllDay: true, googleTaskListId: 'shop', googleTaskListTitle: 'Shopping' }),
      event('w1', 'Weekly sync', 2, { seriesId: 'weekly', isExternal: true }),
      event('w2', 'Weekly sync', 9, { seriesId: 'weekly', isExternal: true }),
      event('e', 'Dentist', 3),
    ];
  });

  afterEach(() => alertSpy.mockRestore());

  it('the All tab has two separate clears and a general one, each with the real number of items', async () => {
    await render(<ItemsScreen />);

    await fireEvent.press(screen.getByTestId('items-clear-button'));

    expect(screen.getByText('Clear tasks (2)')).toBeTruthy();
    expect(screen.getByText('Clear events (3)')).toBeTruthy(); // all three, although the weekly series shows as one card
    expect(screen.getByText('Clear all items (5)')).toBeTruthy();
  });

  it('"Clear tasks" deletes every task, the imported ones included, after asking, and mentions what comes back from Google', async () => {
    await render(<ItemsScreen />);
    await fireEvent.press(screen.getByTestId('items-clear-button'));

    await fireEvent.press(screen.getByTestId('clear-tasks'));

    expect(alertSpy.mock.calls[0][0]).toBe('Delete all 2 tasks?');
    expect(alertSpy.mock.calls[0][1]).toMatch(/come back at the next sync/);
    await pressAlertButton('Delete all');
    expect(mockedDelete).toHaveBeenCalledWith('t', ['a', 'g']);
  });

  it('"Clear events" deletes every occurrence of every event', async () => {
    await render(<ItemsScreen />);
    await fireEvent.press(screen.getByTestId('items-clear-button'));
    await fireEvent.press(screen.getByTestId('clear-events'));

    await pressAlertButton('Delete all');

    expect(mockedDelete).toHaveBeenCalledWith('t', ['w1', 'w2', 'e']);
  });

  it('"Clear all items" deletes everything', async () => {
    await render(<ItemsScreen />);
    await fireEvent.press(screen.getByTestId('items-clear-button'));
    await fireEvent.press(screen.getByTestId('clear-all'));

    expect(alertSpy.mock.calls[0][0]).toBe('Delete all 5 items?');
    await pressAlertButton('Delete all');

    expect(mockedDelete).toHaveBeenCalledWith('t', ['a', 'g', 'w1', 'w2', 'e']);
  });

  it('the Tasks tab offers one clear (all tasks) and the Events tab one (all events)', async () => {
    await render(<ItemsScreen />);

    await fireEvent.press(screen.getByTestId('items-tab-tasks'));
    await fireEvent.press(screen.getByTestId('items-clear-button'));
    expect(screen.getByText('Clear all tasks (2)')).toBeTruthy();
    expect(screen.queryByTestId('clear-events')).toBeNull();
    expect(screen.queryByTestId('clear-all')).toBeNull();
    await fireEvent.press(screen.getByTestId('option-sheet-cancel'));

    await fireEvent.press(screen.getByTestId('items-tab-events'));
    await fireEvent.press(screen.getByTestId('items-clear-button'));
    expect(screen.getByText('Clear all events (3)')).toBeTruthy();
    expect(screen.queryByTestId('clear-tasks')).toBeNull();
  });

  it('nothing is deleted until the person confirms', async () => {
    await render(<ItemsScreen />);
    await fireEvent.press(screen.getByTestId('items-clear-button'));
    await fireEvent.press(screen.getByTestId('clear-all'));

    expect(mockedDelete).not.toHaveBeenCalled();
  });

  it('there is no Clear button on an empty screen', async () => {
    mockItems = [];

    await render(<ItemsScreen />);

    expect(screen.queryByTestId('items-clear-button')).toBeNull();
  });
});

describe('ItemsScreen - own lists', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    alertSpy = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);
    mockedDeleteList.mockResolvedValue(undefined);
    mockLists = [sprint, standups];
    mockItems = [
      mockItem({ id: 'a', title: 'Write report' }),
      mockItem({ id: 'b', title: 'Pay invoice', itemListId: 'l-sprint' }),
      event('e', 'Standup Monday', 2, { itemListId: 'l-standups' }),
    ];
  });

  afterEach(() => alertSpy.mockRestore());

  it('shows each list as a collapsed group with its count, even an empty one, and keeps loose items as they were', async () => {
    mockLists = [sprint, standups, { ...sprint, id: 'l-empty', name: 'Someday' }];

    await render(<ItemsScreen />);

    expect(screen.getByText('Write report')).toBeTruthy();
    expect(screen.getByText('Sprint')).toBeTruthy();
    expect(screen.getByText('1 task')).toBeTruthy();
    expect(screen.getByText('Standups')).toBeTruthy();
    expect(screen.getByText('1 event')).toBeTruthy();
    expect(screen.getByText('0 tasks')).toBeTruthy();
    expect(screen.queryByText('Pay invoice')).toBeNull(); // collapsed
    await fireEvent.press(screen.getByTestId('list-l-sprint-header'));
    expect(screen.getByText('Pay invoice')).toBeTruthy();
  });

  it('a long press on the header of a list opens Rename / Delete list; deleting asks first and keeps the items', async () => {
    await render(<ItemsScreen />);

    await fireEvent(screen.getByTestId('list-l-sprint-header'), 'longPress');
    await fireEvent.press(screen.getByTestId('list-menu-delete'));

    expect(alertSpy.mock.calls[0][0]).toBe('Delete list "Sprint"?');
    expect(alertSpy.mock.calls[0][1]).toMatch(/items in it are kept/);
    expect(mockedDeleteList).not.toHaveBeenCalled();
    await pressAlertButton('Delete list');
    expect(mockedDeleteList).toHaveBeenCalledWith('t', 'l-sprint');
    expect(mockReload).toHaveBeenCalled();
  });

  it('Rename opens the list form with the current name', async () => {
    await render(<ItemsScreen />);

    await fireEvent(screen.getByTestId('list-l-sprint-header'), 'longPress');
    await fireEvent.press(screen.getByTestId('list-menu-rename'));

    expect(screen.getByText('Rename list')).toBeTruthy();
    expect(screen.getByTestId('list-name-input').props.value).toBe('Sprint');
  });

  it('"+ New list" opens the form for a new list (a meeting list while the Events tab is open)', async () => {
    await render(<ItemsScreen />);
    await fireEvent.press(screen.getByTestId('items-tab-events'));

    await fireEvent.press(screen.getByTestId('create-list-button'));

    expect(screen.getByText('New list')).toBeTruthy();
    expect(screen.getByTestId(`list-kind-${ItemListKind.MeetingList}`)).toBeTruthy();
  });
});
