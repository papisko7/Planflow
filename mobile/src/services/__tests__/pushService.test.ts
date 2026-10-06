import { Platform } from 'react-native';
import Constants, { ExecutionEnvironment } from 'expo-constants';
import * as Notifications from 'expo-notifications';
import { registerPushToken, unregisterPushToken } from '../notificationService';
import { configureNotificationHandler, onPushReceived, onPushTapped, registerForPush, setAppBadge, unregisterPush } from '../pushService';

jest.mock('../notificationService');

// Controllable stand-ins: the tests flip these between cases (what kind of app / phone this is).
const mockDevice = { isDevice: true };
jest.mock('expo-device', () => ({ __esModule: true, get isDevice() { return mockDevice.isDevice; } }));
jest.mock('expo-constants', () => ({
  __esModule: true,
  default: { expoConfig: null, easConfig: null, executionEnvironment: 'standalone' },
  ExecutionEnvironment: { StoreClient: 'storeClient', Standalone: 'standalone', Bare: 'bare' },
}));

const mockedRegisterToken = registerPushToken as jest.MockedFunction<typeof registerPushToken>;
const mockedUnregisterToken = unregisterPushToken as jest.MockedFunction<typeof unregisterPushToken>;
const mockedPermissions = Notifications.getPermissionsAsync as jest.Mock;
const mockedRequest = Notifications.requestPermissionsAsync as jest.Mock;
const mockedToken = Notifications.getExpoPushTokenAsync as jest.Mock;
const constants = Constants as unknown as { expoConfig: unknown; easConfig: unknown; executionEnvironment: string };

const withProject = () => {
  constants.expoConfig = { extra: { eas: { projectId: 'project-1' } } };
  constants.executionEnvironment = ExecutionEnvironment.Standalone;
};

describe('pushService', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    Platform.OS = 'ios';
    mockDevice.isDevice = true;
    constants.easConfig = undefined;
    withProject();
    mockedPermissions.mockResolvedValue({ granted: true, canAskAgain: true });
    mockedRequest.mockResolvedValue({ granted: true });
    mockedToken.mockResolvedValue({ data: 'ExponentPushToken[abc]' });
    mockedRegisterToken.mockResolvedValue(undefined);
    mockedUnregisterToken.mockResolvedValue(undefined);
  });

  it('gets the Expo token and tells the server about this phone', async () => {
    expect(await registerForPush()).toBe('registered');

    expect(mockedToken).toHaveBeenCalledWith({ projectId: 'project-1' });
    expect(mockedRegisterToken).toHaveBeenCalledWith('ExponentPushToken[abc]', 'ios');
  });

  it('asks for the system permission when it was not given yet', async () => {
    mockedPermissions.mockResolvedValue({ granted: false, canAskAgain: true });

    expect(await registerForPush()).toBe('registered');

    expect(mockedRequest).toHaveBeenCalled();
  });

  it('reports "denied" and does not register when the user refuses', async () => {
    mockedPermissions.mockResolvedValue({ granted: false, canAskAgain: true });
    mockedRequest.mockResolvedValue({ granted: false });

    expect(await registerForPush()).toBe('denied');
    expect(mockedRegisterToken).not.toHaveBeenCalled();
  });

  it('does not nag again once the system says it cannot ask any more', async () => {
    mockedPermissions.mockResolvedValue({ granted: false, canAskAgain: false });

    expect(await registerForPush()).toBe('denied');
    expect(mockedRequest).not.toHaveBeenCalled();
  });

  it('explains the missing Expo project id instead of failing', async () => {
    constants.expoConfig = {};
    constants.easConfig = undefined;

    expect(await registerForPush()).toBe('noProjectId');
    expect(mockedToken).not.toHaveBeenCalled();
  });

  it('knows that Expo Go on Android cannot receive remote pushes', async () => {
    Platform.OS = 'android';
    constants.executionEnvironment = ExecutionEnvironment.StoreClient;

    expect(await registerForPush()).toBe('expoGoAndroid');
    expect(mockedToken).not.toHaveBeenCalled();
  });

  it('never touches expo-notifications in Expo Go on Android (loading it there raises a red-screen error)', async () => {
    Platform.OS = 'android';
    constants.executionEnvironment = ExecutionEnvironment.StoreClient;
    const received = jest.fn();

    configureNotificationHandler();
    onPushReceived(received)();
    onPushTapped(received)();
    await setAppBadge(3);

    expect(Notifications.setNotificationHandler).not.toHaveBeenCalled();
    expect(Notifications.addNotificationReceivedListener).not.toHaveBeenCalled();
    expect(Notifications.addNotificationResponseReceivedListener).not.toHaveBeenCalled();
    expect(Notifications.setBadgeCountAsync).not.toHaveBeenCalled();
  });

  it('knows that the iOS simulator has no push', async () => {
    mockDevice.isDevice = false;

    expect(await registerForPush()).toBe('simulator');
  });

  it('turns a network failure into an "error" status instead of throwing', async () => {
    mockedToken.mockRejectedValue(new Error('offline'));

    expect(await registerForPush()).toBe('error');
  });

  it('removes this phone from the server on sign-out - once, and never throws', async () => {
    await registerForPush();
    mockedUnregisterToken.mockRejectedValue(new Error('offline'));

    await expect(unregisterPush()).resolves.toBeUndefined();
    await unregisterPush(); // second call: nothing left to remove

    expect(mockedUnregisterToken).toHaveBeenCalledTimes(1);
    expect(mockedUnregisterToken).toHaveBeenCalledWith('ExponentPushToken[abc]');
  });
});
