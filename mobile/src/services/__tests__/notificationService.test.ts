import { http } from '../httpClient';
import {
  clearNotifications,
  deleteNotification,
  deleteNotifications,
  markNotificationsRead,
} from '../notificationService';

jest.mock('../httpClient', () => ({ http: { post: jest.fn(), delete: jest.fn() } }));
jest.mock('../tokenStorage', () => ({ loadSession: jest.fn() }));

const mockedPost = http.post as jest.Mock;
const mockedDelete = http.delete as jest.Mock;

describe('inbox clean-up requests', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedPost.mockResolvedValue({});
    mockedDelete.mockResolvedValue({});
  });

  it('deletes one entry by its id', async () => {
    await deleteNotification('n1');

    expect(mockedDelete).toHaveBeenCalledWith('/api/notifications/n1');
  });

  it('deletes the ticked entries with a single request', async () => {
    await deleteNotifications(['n1', 'n2']);

    expect(mockedPost).toHaveBeenCalledWith('/api/notifications/delete', { ids: ['n1', 'n2'] });
  });

  it('marks the ticked entries read with a single request', async () => {
    await markNotificationsRead(['n1', 'n2']);

    expect(mockedPost).toHaveBeenCalledWith('/api/notifications/read', { ids: ['n1', 'n2'] });
  });

  it('clears the whole inbox', async () => {
    await clearNotifications();

    expect(mockedDelete).toHaveBeenCalledWith('/api/notifications');
  });
});
