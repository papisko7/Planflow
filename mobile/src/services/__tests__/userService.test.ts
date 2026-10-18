import { http } from '../httpClient';
import { deviceTimeZone, forgetReportedTimeZone, reportTimeZone } from '../userService';

jest.mock('../httpClient', () => ({ http: { put: jest.fn() } }));
jest.mock('../tokenStorage', () => ({ loadSession: jest.fn() }));

const mockedPut = http.put as jest.Mock;

describe('reportTimeZone', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    forgetReportedTimeZone();
    mockedPut.mockResolvedValue({});
  });

  it('sends the phone time zone to the server', async () => {
    await reportTimeZone();

    expect(mockedPut).toHaveBeenCalledWith('/api/users/me/time-zone', { timeZoneId: deviceTimeZone() });
  });

  it('reports only once per sign-in, not on every screen', async () => {
    await reportTimeZone();
    await reportTimeZone();

    expect(mockedPut).toHaveBeenCalledTimes(1);
  });

  it('reports again after sign-out, because the next account on the phone has not told the server yet', async () => {
    await reportTimeZone();
    forgetReportedTimeZone();
    await reportTimeZone();

    expect(mockedPut).toHaveBeenCalledTimes(2);
  });

  it('never throws when the server is unreachable, and retries next time', async () => {
    mockedPut.mockRejectedValueOnce(new Error('offline'));

    await expect(reportTimeZone()).resolves.toBeUndefined();
    await reportTimeZone();

    expect(mockedPut).toHaveBeenCalledTimes(2);
  });
});
