// React Native's Modal uses Animated timers (animationType="fade"/"slide") that act() in React 19
// waits for indefinitely. Replacing Modal with a synchronous stand-in that renders children when
// visible and nothing otherwise keeps tests fast and predictable.
// We mutate the already-mocked module (jest-expo's mock runs first) rather than re-mocking it,
// so we avoid loading the actual react-native which requires unavailable TurboModules.
const _RN = require('react-native');
const _React = require('react');
function _MockModal({ visible, children }) {
  return visible ? _React.createElement(_React.Fragment, null, children) : null;
}
_MockModal.displayName = 'Modal';
_RN.Modal = _MockModal;

// The native notification modules do not exist under Jest. Every test gets harmless stand-ins; a test that cares about
// their behaviour (pushService.test.ts) overrides them.
jest.mock('expo-notifications', () => ({
  setNotificationHandler: jest.fn(),
  getPermissionsAsync: jest.fn(async () => ({ granted: true, canAskAgain: true })),
  requestPermissionsAsync: jest.fn(async () => ({ granted: true })),
  getExpoPushTokenAsync: jest.fn(async () => ({ data: 'ExponentPushToken[test-token]' })),
  setNotificationChannelAsync: jest.fn(async () => null),
  setBadgeCountAsync: jest.fn(async () => true),
  addNotificationReceivedListener: jest.fn(() => ({ remove: jest.fn() })),
  addNotificationResponseReceivedListener: jest.fn(() => ({ remove: jest.fn() })),
  getLastNotificationResponseAsync: jest.fn(async () => null),
  AndroidImportance: { MAX: 5 },
}));

jest.mock('expo-device', () => ({ isDevice: true }));

// The icon fonts need native asset loading that does not exist under Jest; a plain text stand-in is enough for tests.
jest.mock('@expo/vector-icons', () => {
  const React = require('react');
  const { Text } = require('react-native');
  const Icon = ({ name }) => React.createElement(Text, null, name);
  return { Ionicons: Icon };
});
