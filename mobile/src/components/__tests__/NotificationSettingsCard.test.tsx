import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { NotificationSettingsCard } from '../NotificationSettingsCard';
import {
  getNotificationPreferences,
  updateNotificationPreferences,
  updateTeamNotificationSettings,
} from '../../services/notificationService';
import { registerForPush } from '../../services/pushService';
import { getMyTeams } from '../../services/teamService';
import { LoginNotificationMode, type NotificationPreferences } from '../../types/notification';
import type { TeamDto } from '../../types/team';

jest.mock('../../services/notificationService');
jest.mock('../../services/teamService');
jest.mock('../../services/pushService');

const mockedGet = getNotificationPreferences as jest.MockedFunction<typeof getNotificationPreferences>;
const mockedUpdate = updateNotificationPreferences as jest.MockedFunction<typeof updateNotificationPreferences>;
const mockedTeamUpdate = updateTeamNotificationSettings as jest.MockedFunction<typeof updateTeamNotificationSettings>;
const mockedTeams = getMyTeams as jest.MockedFunction<typeof getMyTeams>;
const mockedRegister = registerForPush as jest.MockedFunction<typeof registerForPush>;

const defaults: NotificationPreferences = {
  emailEnabled: true,
  pushEnabled: true,
  loginAlerts: LoginNotificationMode.NewDevice,
  taskAssignments: true,
  taskChanges: true,
  teamTasks: false,
  taskUrgency: true,
  teamMembership: true,
  chatDigest: false,
  calendarIssues: true,
};

const personal: TeamDto = { id: 'p', name: 'Me', description: null, memberCount: 1, isPersonal: true, muteNotifications: false, muteChat: false };
const squad: TeamDto = { id: 's', name: 'Squad', description: null, memberCount: 3, isPersonal: false, muteNotifications: false, muteChat: false };

describe('NotificationSettingsCard', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedGet.mockResolvedValue(defaults);
    mockedTeams.mockResolvedValue([personal, squad]);
    mockedUpdate.mockImplementation(async (next) => next);
    mockedTeamUpdate.mockResolvedValue(undefined);
    mockedRegister.mockResolvedValue('registered');
  });

  it('shows the saved settings', async () => {
    await render(<NotificationSettingsCard />);

    await waitFor(() => expect(screen.getByTestId('notif-master-switch').props.value).toBe(true));
    expect(screen.getByTestId('notif-switch-teamTasks').props.value).toBe(false);
    expect(screen.getByTestId('notif-switch-taskAssignments').props.value).toBe(true);
  });

  it('saves a single category switch with the rest unchanged', async () => {
    await render(<NotificationSettingsCard />);
    await waitFor(() => screen.getByTestId('notif-switch-teamTasks'));

    await fireEvent(screen.getByTestId('notif-switch-teamTasks'), 'valueChange', true);

    await waitFor(() => expect(mockedUpdate).toHaveBeenCalledWith({ ...defaults, teamTasks: true }));
    expect(screen.getByTestId('notif-switch-teamTasks').props.value).toBe(true);
  });

  it('switching e-mail off hides only the sign-in alerts - the categories still steer the inbox and push', async () => {
    await render(<NotificationSettingsCard />);
    await waitFor(() => screen.getByTestId('notif-master-switch'));

    await fireEvent(screen.getByTestId('notif-master-switch'), 'valueChange', false);

    await waitFor(() => expect(mockedUpdate).toHaveBeenCalledWith({ ...defaults, emailEnabled: false }));
    await waitFor(() => expect(screen.queryByTestId('notif-login-1')).toBeNull());
    expect(screen.getByTestId('notif-switch-taskChanges')).toBeTruthy();
    expect(screen.getByText(/always sent/i)).toBeTruthy();
  });

  it('saves the push switch on its own and explains what this phone can do', async () => {
    await render(<NotificationSettingsCard />);
    await waitFor(() => screen.getByTestId('notif-push-switch'));

    expect(screen.getByTestId('notif-push-switch').props.value).toBe(true);
    await waitFor(() => expect(screen.getByTestId('push-status').props.children).toMatch(/receives push/));

    await fireEvent(screen.getByTestId('notif-push-switch'), 'valueChange', false);

    await waitFor(() => expect(mockedUpdate).toHaveBeenCalledWith({ ...defaults, pushEnabled: false }));
  });

  it('tells the user why this phone cannot get pushes', async () => {
    mockedRegister.mockResolvedValue('expoGoAndroid');
    await render(<NotificationSettingsCard />);

    await waitFor(() => expect(screen.getByTestId('push-status').props.children).toMatch(/Expo Go on Android/));
  });

  it('changes the sign-in alert mode', async () => {
    await render(<NotificationSettingsCard />);
    await waitFor(() => screen.getByTestId('notif-login-1'));

    await fireEvent.press(screen.getByTestId('notif-login-1'));

    await waitFor(() => expect(mockedUpdate).toHaveBeenCalledWith({ ...defaults, loginAlerts: LoginNotificationMode.EveryLogin }));
  });

  it('lists only real teams for muting (not the personal workspace) and saves a mute', async () => {
    await render(<NotificationSettingsCard />);
    await waitFor(() => screen.getByTestId('notif-team-tasks-s'));

    expect(screen.queryByTestId('notif-team-tasks-p')).toBeNull();

    await fireEvent(screen.getByTestId('notif-team-chat-s'), 'valueChange', true);

    await waitFor(() => expect(mockedTeamUpdate).toHaveBeenCalledWith('s', { muteNotifications: false, muteChat: true }));
  });

  it('puts the switch back and shows an error when saving fails', async () => {
    mockedUpdate.mockRejectedValue({ status: 500, message: 'Server exploded', isNetworkError: false });
    await render(<NotificationSettingsCard />);
    await waitFor(() => screen.getByTestId('notif-switch-chatDigest'));

    await fireEvent(screen.getByTestId('notif-switch-chatDigest'), 'valueChange', true);

    await waitFor(() => expect(screen.getByText('Server exploded')).toBeTruthy());
    expect(screen.getByTestId('notif-switch-chatDigest').props.value).toBe(false);
  });
});
