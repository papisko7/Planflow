import type { NavigatorScreenParams } from '@react-navigation/native';

// Shown instead of the main app when there is no session (see SessionContext/RootNavigator).
export type AuthStackParamList = {
  Login: undefined;
  Register: undefined;
  ForgotPassword: undefined;
};

// Shared param list for the root bottom-tab navigator, kept next to the navigator that uses it.
export type RootTabParamList = {
  Dashboard: undefined;
  Tasks: undefined;
  Teams: undefined;
  Calendar: undefined;
  Settings: undefined;
};

// Top-level stack: the tab navigator is one screen, with detail screens pushed on top of it
// so they get native push/back behavior instead of living inside a tab.
export type RootStackParamList = {
  // Dashboard shortcuts jump straight to another tab: navigate('Tabs', { screen: 'Calendar' }).
  Tabs: NavigatorScreenParams<RootTabParamList> | undefined;
  // edit: open the screen with the edit form already showing (used by the swipe-left gesture).
  TaskDetail: { taskId: string; teamId: string; edit?: boolean };
  Chat: { teamId: string; teamName: string };
  // The in-app notification inbox (opened from the bell on the Dashboard or by tapping a push).
  Notifications: undefined;
};
