import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import SettingsScreen from '../SettingsScreen';
import { getProfile } from '../../services/userService';
import { getNotificationPreferences } from '../../services/notificationService';
import { isPinEnabled } from '../../services/pinService';
import { getMyTeams } from '../../services/teamService';
import { registerForPush } from '../../services/pushService';
import { LoginNotificationMode } from '../../types/notification';

jest.mock('../../services/userService');
jest.mock('../../services/notificationService');
jest.mock('../../services/pinService');
jest.mock('../../services/teamService');
jest.mock('../../services/pushService');
jest.mock('../../context/SessionContext', () => ({ useSession: () => ({ signOut: jest.fn() }) }));

describe('SettingsScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    (getProfile as jest.Mock).mockResolvedValue({ id: 'u', email: 'ann@example.com', displayName: 'Ann', emailVerified: true });
    (getNotificationPreferences as jest.Mock).mockResolvedValue({
      emailEnabled: true, pushEnabled: true, loginAlerts: LoginNotificationMode.NewDevice,
      taskAssignments: true, taskChanges: true, teamTasks: false, taskUrgency: true, teamMembership: true, chatDigest: false, calendarIssues: true, incompleteItemsReminder: true,
    });
    (getMyTeams as jest.Mock).mockResolvedValue([]);
    (isPinEnabled as jest.Mock).mockResolvedValue(true);
    (registerForPush as jest.Mock).mockResolvedValue('registered');
  });

  it('shows three collapsed sections with a one-line summary each, not a wall of switches and fields', async () => {
    await render(<SettingsScreen />);

    await waitFor(() => expect(screen.getByText('Ann')).toBeTruthy()); // Profile summary
    expect(screen.getByText('Profile')).toBeTruthy();
    expect(screen.getByText('Credentials')).toBeTruthy();
    expect(screen.getByText('Notifications')).toBeTruthy();
    await waitFor(() => expect(screen.getByText('PIN login on')).toBeTruthy());
    await waitFor(() => expect(screen.getByText('8 of 10 on')).toBeTruthy());

    expect(screen.queryByTestId('profile-name-input')).toBeNull();
    expect(screen.queryByTestId('pin-toggle')).toBeNull();
    expect(screen.queryByTestId('notif-master-switch')).toBeNull();
    expect(screen.getByTestId('settings-signout-button')).toBeTruthy();
  });

  it('Profile opens to the name and the e-mail', async () => {
    await render(<SettingsScreen />);
    await waitFor(() => expect(screen.getByText('Ann')).toBeTruthy());

    await fireEvent.press(screen.getByTestId('profile-section-header'));

    expect(screen.getByTestId('profile-name-input').props.value).toBe('Ann');
    expect(screen.getByTestId('profile-email-input').props.value).toBe('ann@example.com');
    expect(screen.getByTestId('profile-save-button')).toBeTruthy();
  });

  it('Credentials opens to the PIN controls, with the password form folded inside', async () => {
    await render(<SettingsScreen />);

    await fireEvent.press(screen.getByTestId('credentials-section-header'));

    await waitFor(() => expect(screen.getByTestId('pin-toggle')).toBeTruthy());
    expect(screen.getByTestId('pin-change-button')).toBeTruthy();
    expect(screen.queryByTestId('password-current-input')).toBeNull();

    await fireEvent.press(screen.getByTestId('change-password-section-header'));
    expect(screen.getByTestId('password-current-input')).toBeTruthy();
  });
});
