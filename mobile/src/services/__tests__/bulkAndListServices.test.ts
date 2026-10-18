import { http } from '../httpClient';
import { createList, deleteList, getTeamLists, renameList } from '../listService';
import { bulkDeleteTasks, bulkMoveToList, bulkSetStatus, getAllTasks } from '../taskService';
import { getCachedAllTasks, invalidateAllTasksCache } from '../taskCache';
import { getMyTeams } from '../teamService';
import { ItemListKind } from '../../types/itemList';
import { TaskStatus } from '../../types/task';

jest.mock('../httpClient', () => ({ http: { get: jest.fn(), post: jest.fn(), put: jest.fn(), delete: jest.fn() } }));
jest.mock('../authService', () => ({ runInTeamContext: (_teamId: string, action: () => Promise<unknown>) => action() }));
jest.mock('../teamService');

const mockedHttp = http as unknown as { get: jest.Mock; post: jest.Mock; put: jest.Mock; delete: jest.Mock };
const mockedTeams = getMyTeams as jest.MockedFunction<typeof getMyTeams>;

const ids = (n: number) => Array.from({ length: n }, (_, i) => `id${i}`);

describe('bulk actions', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedHttp.post.mockResolvedValue({ data: { succeeded: 0, skippedIds: [] } });
  });

  it('delete posts the ids to the team\'s bulk-delete route', async () => {
    mockedHttp.post.mockResolvedValue({ data: { succeeded: 2, skippedIds: ['x'] } });

    const result = await bulkDeleteTasks('team1', ['a', 'b']);

    expect(mockedHttp.post).toHaveBeenCalledWith('/api/teams/team1/tasks/bulk-delete', { taskIds: ['a', 'b'] });
    expect(result).toEqual({ succeeded: 2, skippedIds: ['x'] });
  });

  it('status sends the status along; moving sends the list, null meaning "no list"', async () => {
    await bulkSetStatus('team1', ['a'], TaskStatus.Completed);
    await bulkMoveToList('team1', ['a'], 'l1');
    await bulkMoveToList('team1', ['a'], null);

    expect(mockedHttp.post).toHaveBeenNthCalledWith(1, '/api/teams/team1/tasks/bulk-status', { taskIds: ['a'], status: TaskStatus.Completed });
    expect(mockedHttp.post).toHaveBeenNthCalledWith(2, '/api/teams/team1/tasks/bulk-move', { taskIds: ['a'], itemListId: 'l1' });
    expect(mockedHttp.post).toHaveBeenNthCalledWith(3, '/api/teams/team1/tasks/bulk-move', { taskIds: ['a'], itemListId: null });
  });

  it('a selection bigger than the server accepts goes in several requests and the answers are added up', async () => {
    mockedHttp.post
      .mockResolvedValueOnce({ data: { succeeded: 1000, skippedIds: [] } })
      .mockResolvedValueOnce({ data: { succeeded: 199, skippedIds: ['id1100'] } });

    const result = await bulkDeleteTasks('team1', ids(1200));

    expect(mockedHttp.post).toHaveBeenCalledTimes(2);
    expect(mockedHttp.post.mock.calls[0][1].taskIds).toHaveLength(1000);
    expect(mockedHttp.post.mock.calls[1][1].taskIds).toHaveLength(200);
    expect(result).toEqual({ succeeded: 1199, skippedIds: ['id1100'] });
  });

  it('drops the cached lists, also when a request fails half way', async () => {
    mockedTeams.mockResolvedValue([]);
    await getAllTasks(true);
    expect(getCachedAllTasks()).not.toBeNull();
    mockedHttp.post.mockRejectedValue(new Error('down'));

    await expect(bulkDeleteTasks('team1', ['a'])).rejects.toThrow('down');

    expect(getCachedAllTasks()).toBeNull();
  });
});

describe('own lists', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    invalidateAllTasksCache();
  });

  it('reads, creates, renames and deletes through the team\'s lists route', async () => {
    mockedHttp.get.mockResolvedValue({ data: [{ id: 'l1' }] });
    mockedHttp.post.mockResolvedValue({ data: { id: 'l2' } });
    mockedHttp.put.mockResolvedValue({ data: { id: 'l2' } });
    mockedHttp.delete.mockResolvedValue({});

    expect(await getTeamLists('team1')).toEqual([{ id: 'l1' }]);
    await createList('team1', 'Sprint', ItemListKind.MeetingList);
    await renameList('team1', 'l2', 'Sprint 2');
    await deleteList('team1', 'l2');

    expect(mockedHttp.get).toHaveBeenCalledWith('/api/teams/team1/lists');
    expect(mockedHttp.post).toHaveBeenCalledWith('/api/teams/team1/lists', { name: 'Sprint', kind: ItemListKind.MeetingList });
    expect(mockedHttp.put).toHaveBeenCalledWith('/api/teams/team1/lists/l2', { name: 'Sprint 2' });
    expect(mockedHttp.delete).toHaveBeenCalledWith('/api/teams/team1/lists/l2');
  });
});

describe('getAllTasks with lists', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    invalidateAllTasksCache();
  });

  const team = { id: 'team1', name: 'Squad' };

  it('brings the team\'s own lists along with its items', async () => {
    mockedTeams.mockResolvedValue([team as never]);
    mockedHttp.get.mockImplementation(async (url: string) =>
      url.endsWith('/tasks') ? { data: [{ id: 't1' }] } : { data: [{ id: 'l1', teamId: 'team1' }] },
    );

    const all = await getAllTasks(true);

    expect(all.tasks).toEqual([{ id: 't1', teamName: 'Squad' }]);
    expect(all.lists).toEqual([{ id: 'l1', teamId: 'team1' }]);
  });

  it('a server without lists just has none - the items still load', async () => {
    mockedTeams.mockResolvedValue([team as never]);
    mockedHttp.get.mockImplementation(async (url: string) => {
      if (url.endsWith('/tasks')) return { data: [{ id: 't1' }] };
      throw { status: 404, message: 'Not found' };
    });

    const all = await getAllTasks(true);

    expect(all.tasks).toHaveLength(1);
    expect(all.lists).toEqual([]);
  });
});
