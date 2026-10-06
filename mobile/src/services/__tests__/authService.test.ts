import { ensureTeamContext, getFreshAccessToken, login, register, requestPasswordReset, resetPassword, setSessionExpiredHandler } from '../authService';

const mockPost = jest.fn();

jest.mock('axios', () => ({
  __esModule: true,
  default: {
    create: () => ({ post: (...args: unknown[]) => mockPost(...args) }),
    isAxiosError: (e: unknown) => (e as { isAxiosError?: boolean })?.isAxiosError === true,
  },
}));
jest.mock('../httpClient', () => {
  let token: string | null = null;
  return {
    API_BASE_URL: 'http://test',
    getAccessToken: () => token,
    setAccessToken: (t: string | null) => {
      token = t;
    },
    setUnauthorizedHandler: jest.fn(),
  };
});
jest.mock('../tokenStorage', () => ({
  saveSession: jest.fn().mockResolvedValue(undefined),
  clearSession: jest.fn().mockResolvedValue(undefined),
  loadSession: jest.fn().mockResolvedValue({ accessToken: 'a', refreshToken: 'r', userId: 'u' }),
  getDeviceId: jest.fn().mockResolvedValue('device-1'),
}));

function authResult(accessToken: string, expiresInMs: number) {
  return {
    data: {
      userId: 'u',
      email: 'e',
      displayName: 'n',
      accessToken,
      accessTokenExpiresAtUtc: new Date(Date.now() + expiresInMs).toISOString(),
      refreshToken: 'r2',
    },
  };
}

const loginRequest = { email: 'e', password: 'p', teamId: 'T' };

beforeEach(() => mockPost.mockReset());

describe('access token freshness', () => {
  it('does not refresh when the token is valid and already scoped to the team', async () => {
    mockPost.mockResolvedValueOnce(authResult('t1', 15 * 60_000));
    await login(loginRequest);

    await ensureTeamContext('T');

    expect(mockPost).toHaveBeenCalledTimes(1); // login only
  });

  it('refreshes an expired token even when the team has not changed', async () => {
    mockPost.mockResolvedValueOnce(authResult('t1', 10_000)); // expires inside the 1-minute margin
    await login(loginRequest);
    mockPost.mockResolvedValueOnce(authResult('t2', 15 * 60_000));

    await ensureTeamContext('T');

    expect(mockPost).toHaveBeenLastCalledWith('/api/auth/refresh', expect.objectContaining({ teamId: 'T' }));
  });

  it('hands SignalR a refreshed token when the current one has expired', async () => {
    mockPost.mockResolvedValueOnce(authResult('t1', -1000));
    await login(loginRequest);
    mockPost.mockResolvedValueOnce(authResult('t2', 15 * 60_000));

    await expect(getFreshAccessToken()).resolves.toBe('t2');
  });
});

describe('dead session handling', () => {
  async function loginWithExpiredToken() {
    mockPost.mockResolvedValueOnce(authResult('t1', -1000));
    await login(loginRequest);
  }

  it('signals session expiry when the refresh is rejected with 401', async () => {
    const onExpired = jest.fn();
    setSessionExpiredHandler(onExpired);
    await loginWithExpiredToken();
    mockPost.mockRejectedValueOnce({ isAxiosError: true, response: { status: 401 } });

    await expect(ensureTeamContext('T')).rejects.toBeDefined();

    expect(onExpired).toHaveBeenCalledTimes(1);
  });

  it('keeps the user signed in when the refresh fails for network reasons', async () => {
    const onExpired = jest.fn();
    setSessionExpiredHandler(onExpired);
    await loginWithExpiredToken();
    mockPost.mockRejectedValueOnce({ isAxiosError: true, response: undefined });

    await expect(ensureTeamContext('T')).rejects.toBeDefined();

    expect(onExpired).not.toHaveBeenCalled();
  });
});

describe('device id and password reset', () => {
  it('sends the installation id and a device name with login and register', async () => {
    mockPost.mockResolvedValue(authResult('t1', 15 * 60_000));

    await login(loginRequest);
    await register({ email: 'e', password: 'p', displayName: 'n' });

    expect(mockPost).toHaveBeenNthCalledWith(1, '/api/auth/login', expect.objectContaining({ deviceId: 'device-1', deviceName: expect.stringContaining('PlanFlow on') }));
    expect(mockPost).toHaveBeenNthCalledWith(2, '/api/auth/register', expect.objectContaining({ deviceId: 'device-1' }));
  });

  it('calls the forgot-password and reset-password endpoints', async () => {
    mockPost.mockResolvedValue({ data: undefined });

    await requestPasswordReset('ann@example.com');
    await resetPassword({ email: 'ann@example.com', code: '123456', newPassword: 'new-password' });

    expect(mockPost).toHaveBeenNthCalledWith(1, '/api/auth/forgot-password', { email: 'ann@example.com' });
    expect(mockPost).toHaveBeenNthCalledWith(2, '/api/auth/reset-password', { email: 'ann@example.com', code: '123456', newPassword: 'new-password' });
  });
});
