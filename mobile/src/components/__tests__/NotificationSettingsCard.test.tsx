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
  incompleteItemsReminder: true,
};

const personal: TeamDto = { id: 'p', name: 'Me', description: null, memberCount: 1, isPersonal: true, muteNotifications: false, muteChat: false };
const squad: TeamDto = { id: 's', name: 'Squad', description: null, memberCount: 3, isPersonal: false, muteNotifications: false, muteChat: false };

// Everything sits in the collapsed Notifications section (the team mutes in a nested Teams one inside it);
// these open them the way a user would.
const openNotifications = async () => {
  await waitFor(() => expect(screen.getByText(/ of 10 on/)).toBeTruthy()); // settings loaded
  await fireEvent.press(screen.getByTestId('notifications-section-header'));
};
const openTeams = () => fireEvent.press(screen.getByTestId('notif-teams-section-header'));

async function renderOpen() {
  await render(<NotificationSettingsCard />);
  await openNotifications();
}

describe('NotificationSettingsCard', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedGet.mockResolvedValue(defaults);
    mockedTeams.mockResolvedValue([personal, squad]);
    mockedUpdate.mockImplementation(async (next) => next);
    mockedTeamUpdate.mockResolvedValue(undefined);
    mockedRegister.mockResolvedValue('registered');
  });

  it('is collapsed by default and summarises how many switches are on', async () => {
    await render(<NotificationSettingsCard />);

    // push, e-mail and 8 categories; the defaults have teamTasks and chatDigest off
    await waitFor(() => expect(screen.getByText('8 of 10 on')).toBeTruthy());
    expect(screen.queryByTestId('notif-master-switch')).toBeNull();

    await openNotifications();
    expect(screen.getByTestId('notif-master-switch')).toBeTruthy();
    expect(screen.queryByTestId('notif-team-tasks-s')).toBeNull(); // the Teams section is collapsed too
    expect(screen.getByText('None muted')).toBeTruthy();
  });

  it('shows the saved settings', async () => {
    await renderOpen();

    expect(screen.getByTestId('notif-master-switch').props.value).toBe(true);
    expect(screen.getByTestId('notif-switch-teamTasks').props.value).toBe(false);
    expect(screen.getByTestId('notif-switch-taskAssignments').props.value).toBe(true);
  });

  it('saves a single category switch with the rest unchanged', async () => {
    await renderOpen();

    await fireEvent(screen.getByTestId('notif-switch-teamTasks'), 'valueChange', true);

    await waitFor(() => expect(mockedUpdate).toHaveBeenCalledWith({ ...defaults, teamTasks: true }));
    expect(screen.getByTestId('notif-switch-teamTasks').props.value).toBe(true);
  });

  it('has a switch for the daily reminder about incomplete items, on by default, that says it is in-app only', async () => {
    await renderOpen();

    expect(screen.getByTestId('notif-switch-incompleteItemsReminder').props.value).toBe(true);
    expect(screen.getByText(/In the app and on the phone only/)).toBeTruthy();

    await fireEvent(screen.getByTestId('notif-switch-incompleteItemsReminder'), 'valueChange', false);

    await waitFor(() => expect(mockedUpdate).toHaveBeenCalledWith({ ...defaults, incompleteItemsReminder: false }));
  });

  it('switching e-mail off hides only the sign-in alerts - the categories still steer the inbox and push', async () => {
    await renderOpen();

    await fireEvent(screen.getByTestId('notif-master-switch'), 'valueChange', false);

    await waitFor(() => expect(mockedUpdate).toHaveBeenCalledWith({ ...defaults, emailEnabled: false }));
    await waitFor(() => expect(screen.queryByTestId('notif-login-1')).toBeNull());
    expect(screen.getByTestId('notif-switch-taskChanges')).toBeTruthy();
    expect(screen.getByText(/always sent/i)).toBeTruthy();
  });

  it('saves the push switch on its own and explains what this phone can do', async () => {
    await renderOpen();

    expect(screen.getByTestId('notif-push-switch').props.value).toBe(true);
    await waitFor(() => expect(screen.getByTestId('push-status').props.children).toMatch(/receives push/));

    await fireEvent(screen.getByTestId('notif-push-switch'), 'valueChange', false);

    await waitFor(() => expect(mockedUpdate).toHaveBeenCalledWith({ ...defaults, pushEnabled: false }));
  });

  it('tells the user why this phone cannot get pushes', async () => {
    mockedRegister.mockResolvedValue('expoGoAndroid');
    await renderOpen();

    await waitFor(() => expect(screen.getByTestId('push-status').props.children).toMatch(/Expo Go on Android/));
  });

  it('changes the sign-in alert mode', async () => {
    await renderOpen();

    await fireEvent.press(screen.getByTestId('notif-login-1'));

    await waitFor(() => expect(mockedUpdate).toHaveBeenCalledWith({ ...defaults, loginAlerts: LoginNotificationMode.EveryLogin }));
  });

  it('lists only real teams for muting (not the personal workspace) and saves a mute', async () => {
    await renderOpen();
    await openTeams();

    expect(screen.getByTestId('notif-team-tasks-s')).toBeTruthy();
    expect(screen.queryByTestId('notif-team-tasks-p')).toBeNull();

    await fireEvent(screen.getByTestId('notif-team-chat-s'), 'valueChange', true);

    await waitFor(() => expect(mockedTeamUpdate).toHaveBeenCalledWith('s', { muteNotifications: false, muteChat: true }));
  });

  it('counts muted teams in the collapsed Teams header', async () => {
    mockedTeams.mockResolvedValue([personal, { ...squad, muteChat: true }]);
    await renderOpen();

    expect(screen.getByText('1 muted')).toBeTruthy();
  });

  it('puts the switch back and shows an error when saving fails', async () => {
    mockedUpdate.mockRejectedValue({ status: 500, message: 'Server exploded', isNetworkError: false });
    await renderOpen();

    await fireEvent(screen.getByTestId('notif-switch-chatDigest'), 'valueChange', true);

    await waitFor(() => expect(screen.getByText('Server exploded')).toBeTruthy());
    expect(screen.getByTestId('notif-switch-chatDigest').props.value).toBe(false);
  });
});
