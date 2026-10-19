import type { NavigatorScreenParams } from '@react-navigation/native';

export type AuthStackParamList = {
  Login: undefined;
  Register: undefined;
  ForgotPassword: undefined;
};

export type RootTabParamList = {
  Dashboard: undefined;
  Items: undefined;
  Teams: undefined;
  Calendar: undefined;
  Settings: undefined;
};

export type RootStackParamList = {
  Tabs: NavigatorScreenParams<RootTabParamList> | undefined;
  TaskDetail: { taskId: string; teamId: string; edit?: boolean };
  Chat: { teamId: string; teamName: string };
  Notifications: undefined;
};
